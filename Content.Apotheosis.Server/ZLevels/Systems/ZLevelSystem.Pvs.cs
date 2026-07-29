using System.Numerics;
using Content.Apotheosis.Shared.ZLevels;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Apotheosis.Server.ZLevels;

public sealed partial class ZLevelSystem
{
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionProjectionSources = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionProjectedEntities = new();
    private readonly HashSet<ICommonSession> _seenSessions = new();
    private readonly List<ICommonSession> _removedSessions = new();
    private readonly HashSet<EntityUid> _projectedCandidates = new();
    private readonly HashSet<EntityUid> _desiredProjectedEntities = new();
    private readonly HashSet<EntityUid> _desiredProjectionSources = new();
    private readonly List<Entity<MapGridComponent>> _visibleProjectionGrids = new();
    private readonly List<EntityUid> _removedEntities = new();

    private float _pvsAccumulator;

    private const float PvsUpdateInterval = 0.5f;
    private const float ProjectedEntityRange = 14f;
    private const int MaxProjectedEntitiesPerSession = 3000;

    private void InitializePvs()
    {
        SubscribeLocalEvent<ZLevelTransitionedEvent>(OnZLevelTransitioned);
    }

    private void OnZLevelTransitioned(ZLevelTransitionedEvent args)
    {
        if (!TryComp(args.SourceMap, out MapProjectionComponent? projection) ||
            projection.SourceMap != args.DestinationMap ||
            !projection.RenderEntities)
        {
            return;
        }

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out var actor, out var transform))
        {
            if (transform.MapUid != args.SourceMap)
                continue;

            var actorPosition = _transform.GetMapCoordinates(transform).Position;
            if ((actorPosition - args.MapPosition).LengthSquared() >
                ProjectedEntityRange * ProjectedEntityRange ||
                !CanSeeLowerEntity(args.SourceMap, args.MapPosition))
            {
                continue;
            }

            var session = actor.PlayerSession;
            if (!_sessionProjectedEntities.TryGetValue(session, out var entities))
            {
                entities = new HashSet<EntityUid>();
                _sessionProjectedEntities.Add(session, entities);
            }

            if (entities.Add(args.Entity))
                _pvs.AddSessionDirectOverride(args.Entity, session);
        }
    }

    private void ShutdownPvs()
    {
        foreach (var (session, sources) in _sessionProjectionSources)
        {
            foreach (var uid in sources)
                _pvs.RemoveForceSend(uid, session);
        }

        foreach (var (session, entities) in _sessionProjectedEntities)
        {
            foreach (var uid in entities)
                _pvs.RemoveSessionDirectOverride(uid, session);
        }

        _sessionProjectionSources.Clear();
        _sessionProjectedEntities.Clear();
    }

    private void UpdatePvs(float frameTime)
    {
        _pvsAccumulator += frameTime;
        if (_pvsAccumulator < PvsUpdateInterval)
            return;

        _pvsAccumulator = 0f;
        UpdatePvsSubscriptions();
    }

    private void UpdatePvsSubscriptions()
    {
        _seenSessions.Clear();

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out var actor, out var transform))
        {
            var session = actor.PlayerSession;
            _seenSessions.Add(session);

            if (transform.MapUid is { } upperMap &&
                TryComp(upperMap, out MapProjectionComponent? projection) &&
                projection.SourceMap is { } lowerMap &&
                TryComp(lowerMap, out MapComponent? lowerMapComponent))
            {
                var actorPosition = _transform.GetMapCoordinates(transform).Position;
                var bounds = Box2.CenteredAround(
                    actorPosition,
                    new Vector2(ProjectedEntityRange * 2f));

                UpdateProjectionSources(session, lowerMap, lowerMapComponent, bounds);
                UpdateProjectedEntities(
                    session,
                    upperMap,
                    lowerMap,
                    lowerMapComponent,
                    projection,
                    bounds);
            }
            else
            {
                ClearProjectionSources(session);
                ClearProjectedEntities(session);
            }
        }

        _removedSessions.Clear();
        foreach (var session in _sessionProjectionSources.Keys)
        {
            if (!_seenSessions.Contains(session))
                _removedSessions.Add(session);
        }

        foreach (var session in _sessionProjectedEntities.Keys)
        {
            if (!_seenSessions.Contains(session) && !_removedSessions.Contains(session))
                _removedSessions.Add(session);
        }

        foreach (var session in _removedSessions)
        {
            ClearProjectionSources(session);
            ClearProjectedEntities(session);
        }
    }

    private void UpdateProjectionSources(
        ICommonSession session,
        EntityUid sourceMap,
        MapComponent sourceMapComponent,
        Box2 bounds)
    {
        _desiredProjectionSources.Clear();
        _desiredProjectionSources.Add(sourceMap);
        _visibleProjectionGrids.Clear();
        var visibleGrids = _visibleProjectionGrids;
        _map.FindGridsIntersecting(
            sourceMapComponent.MapId,
            bounds,
            ref visibleGrids,
            approx: true,
            includeMap: false);

        foreach (var grid in visibleGrids)
            _desiredProjectionSources.Add(grid.Owner);

        if (!_sessionProjectionSources.TryGetValue(session, out var current))
        {
            current = new HashSet<EntityUid>();
            _sessionProjectionSources.Add(session, current);
        }

        _removedEntities.Clear();
        foreach (var uid in current)
        {
            if (!_desiredProjectionSources.Contains(uid))
                _removedEntities.Add(uid);
        }

        foreach (var uid in _removedEntities)
        {
            _pvs.RemoveForceSend(uid, session);
            current.Remove(uid);
        }

        foreach (var uid in _desiredProjectionSources)
        {
            if (current.Add(uid))
                _pvs.AddForceSend(uid, session);
        }
    }

    private void UpdateProjectedEntities(
        ICommonSession session,
        EntityUid upperMap,
        EntityUid sourceMap,
        MapComponent sourceMapComponent,
        MapProjectionComponent projection,
        Box2 bounds)
    {
        if (!projection.RenderEntities)
        {
            ClearProjectedEntities(session);
            return;
        }

        _projectedCandidates.Clear();
        _desiredProjectedEntities.Clear();
        _lookup.GetEntitiesIntersecting(
            sourceMapComponent.MapId,
            bounds,
            _projectedCandidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var uid in _projectedCandidates)
        {
            if (_desiredProjectedEntities.Count >= MaxProjectedEntitiesPerSession)
                break;

            if (uid == sourceMap ||
                HasComp<MapGridComponent>(uid) ||
                !TryComp(uid, out TransformComponent? transform) ||
                transform.MapUid != sourceMap)
            {
                continue;
            }

            var mapPosition = _transform.GetMapCoordinates(transform).Position;
            if (CanSeeLowerEntity(upperMap, mapPosition))
                _desiredProjectedEntities.Add(uid);
        }

        if (!_sessionProjectedEntities.TryGetValue(session, out var current))
        {
            current = new HashSet<EntityUid>();
            _sessionProjectedEntities.Add(session, current);
        }

        _removedEntities.Clear();
        foreach (var uid in current)
        {
            if (!_desiredProjectedEntities.Contains(uid))
                _removedEntities.Add(uid);
        }

        foreach (var uid in _removedEntities)
        {
            _pvs.RemoveSessionDirectOverride(uid, session);
            current.Remove(uid);
        }

        foreach (var uid in _desiredProjectedEntities)
        {
            if (current.Add(uid))
                _pvs.AddSessionDirectOverride(uid, session);
        }
    }

    private bool CanSeeLowerEntity(EntityUid upperMap, Vector2 mapPosition)
    {
        if (!TryComp(upperMap, out MapComponent? mapComponent))
            return false;

        var mapCoordinates = new MapCoordinates(mapPosition, mapComponent.MapId);
        if (!_map.TryFindGridAt(mapCoordinates, out var upperGrid, out var upperGridComponent))
            return true;

        var coordinates = _transform.ToCoordinates(upperGrid, mapCoordinates);
        var tile = _map.GetTileRef(upperGrid, upperGridComponent, coordinates).Tile;
        return tile.IsEmpty || _tiles[tile.TypeId].RenderZLevelBelow;
    }

    private void ClearProjectionSources(ICommonSession session)
    {
        if (!_sessionProjectionSources.Remove(session, out var sources))
            return;

        foreach (var uid in sources)
            _pvs.RemoveForceSend(uid, session);
    }

    private void ClearProjectedEntities(ICommonSession session)
    {
        if (!_sessionProjectedEntities.Remove(session, out var entities))
            return;

        foreach (var uid in entities)
            _pvs.RemoveSessionDirectOverride(uid, session);
    }
}
