using System.Numerics;
using Content.Shared._Apotheosis.ZLevels;
using Robust.Server.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server._Apotheosis.ZLevels;

/// <summary>
/// Connects adjacent marked grids and selectively sends lower-level data to nearby clients.
/// </summary>
public sealed partial class ApotheosisZLevelSystem : EntitySystem
{
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private readonly HashSet<string> _dirtyGroups = new();
    private readonly Dictionary<ICommonSession, EntityUid> _sessionSources = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionProjectedEntities = new();
    private readonly HashSet<ICommonSession> _seenSessions = new();
    private readonly List<ICommonSession> _removedSessions = new();
    private readonly HashSet<EntityUid> _projectedCandidates = new();
    private readonly HashSet<EntityUid> _desiredProjectedEntities = new();
    private readonly List<EntityUid> _removedProjectedEntities = new();

    private float _pvsAccumulator;
    private const float PvsUpdateInterval = 0.5f;
    private const float ProjectedEntityRange = 14f;
    private const int MaxProjectedEntitiesPerSession = 3000;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ApotheosisZLevelComponent, ComponentStartup>(OnLevelChanged);
        SubscribeLocalEvent<ApotheosisZLevelComponent, ComponentShutdown>(OnLevelChanged);
    }

    public override void Shutdown()
    {
        foreach (var (session, source) in _sessionSources)
        {
            _pvs.RemoveForceSend(source, session);
        }

        foreach (var (session, entities) in _sessionProjectedEntities)
        {
            foreach (var uid in entities)
            {
                _pvs.RemoveSessionDirectOverride(uid, session);
            }
        }

        _sessionSources.Clear();
        _sessionProjectedEntities.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        RebuildDirtyGroups();

        _pvsAccumulator += frameTime;
        if (_pvsAccumulator < PvsUpdateInterval)
            return;

        _pvsAccumulator = 0f;
        UpdatePvsSubscriptions();
    }

    private void OnLevelChanged(
        Entity<ApotheosisZLevelComponent> entity,
        ref ComponentStartup args)
    {
        _dirtyGroups.Add(entity.Comp.Group);
    }

    private void OnLevelChanged(
        Entity<ApotheosisZLevelComponent> entity,
        ref ComponentShutdown args)
    {
        _dirtyGroups.Add(entity.Comp.Group);
    }

    private void RebuildDirtyGroups()
    {
        if (_dirtyGroups.Count == 0)
            return;

        foreach (var group in _dirtyGroups)
        {
            RebuildGroup(group);
        }

        _dirtyGroups.Clear();
    }

    private void RebuildGroup(string group)
    {
        var levels = new Dictionary<int, Entity<ApotheosisZLevelComponent>>();
        var expectedManaged = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ApotheosisZLevelComponent, MapGridComponent>();

        while (query.MoveNext(out var uid, out var level, out _))
        {
            if (level.Group != group)
                continue;

            levels.TryAdd(level.Level, (uid, level));
        }

        foreach (var (_, upper) in levels)
        {
            if (!levels.TryGetValue(upper.Comp.Level - 1, out var lower))
            {
                RemoveManagedProjection(upper.Owner, group);
                continue;
            }

            LinkGridsCore(
                upper.Owner,
                lower.Owner,
                upper.Comp.Darkness,
                upper.Comp.RenderEntities);
            var managed = EnsureComp<ApotheosisZLevelManagedComponent>(upper.Owner);
            managed.Group = group;
            expectedManaged.Add(upper.Owner);
        }

        var managedQuery = EntityQueryEnumerator<ApotheosisZLevelManagedComponent>();
        while (managedQuery.MoveNext(out var uid, out var managed))
        {
            if (managed.Group != group)
                continue;

            if (!expectedManaged.Contains(uid))
            {
                UnlinkLower(uid);
            }
        }
    }

    private void RemoveManagedProjection(EntityUid uid, string group)
    {
        if (!TryComp(uid, out ApotheosisZLevelManagedComponent? managed) ||
            managed.Group != group)
        {
            return;
        }

        UnlinkLower(uid);
    }

    /// <summary>
    /// Links two arbitrary grids as adjacent levels. Existing conflicting links are replaced.
    /// </summary>
    public bool LinkGrids(
        EntityUid upperGrid,
        EntityUid lowerGrid,
        float darkness = 0.45f,
        bool renderEntities = true)
    {
        if (!LinkGridsCore(upperGrid, lowerGrid, darkness, renderEntities))
            return false;

        RemComp<ApotheosisZLevelManagedComponent>(upperGrid);
        return true;
    }

    private bool LinkGridsCore(
        EntityUid upperGrid,
        EntityUid lowerGrid,
        float darkness,
        bool renderEntities)
    {
        if (upperGrid == lowerGrid ||
            !HasComp<MapGridComponent>(upperGrid) ||
            !HasComp<MapGridComponent>(lowerGrid))
        {
            return false;
        }

        if (TryComp(upperGrid, out ApotheosisZLinkComponent? upperLink) &&
            upperLink.LowerGrid is { } oldLower &&
            oldLower != lowerGrid)
        {
            UnlinkLower(upperGrid);
        }

        if (TryComp(lowerGrid, out ApotheosisZLinkComponent? lowerLink) &&
            lowerLink.UpperGrid is { } oldUpper &&
            oldUpper != upperGrid)
        {
            UnlinkLower(oldUpper);
        }

        upperLink = EnsureComp<ApotheosisZLinkComponent>(upperGrid);
        lowerLink = EnsureComp<ApotheosisZLinkComponent>(lowerGrid);
        upperLink.LowerGrid = lowerGrid;
        lowerLink.UpperGrid = upperGrid;

        var projection = EnsureComp<MapGridProjectionComponent>(upperGrid);
        var brightness = Math.Clamp(1f - darkness, 0f, 1f);
        var modulate = new Color(brightness, brightness, brightness, 1f);

        if (projection.SourceGrid != lowerGrid ||
            projection.Modulate != modulate ||
            projection.RenderEntities != renderEntities)
        {
            projection.SourceGrid = lowerGrid;
            projection.Modulate = modulate;
            projection.RenderEntities = renderEntities;
            Dirty(upperGrid, projection);
        }

        return true;
    }

    /// <summary>
    /// Removes the lower link from a grid while preserving its own upper link.
    /// </summary>
    public bool UnlinkLower(EntityUid upperGrid)
    {
        if (!TryComp(upperGrid, out ApotheosisZLinkComponent? upperLink) ||
            upperLink.LowerGrid is not { } lowerGrid)
        {
            RemComp<MapGridProjectionComponent>(upperGrid);
            RemComp<ApotheosisZLevelManagedComponent>(upperGrid);
            return false;
        }

        upperLink.LowerGrid = null;
        if (upperLink.UpperGrid == null)
            RemComp<ApotheosisZLinkComponent>(upperGrid);

        if (TryComp(lowerGrid, out ApotheosisZLinkComponent? lowerLink) &&
            lowerLink.UpperGrid == upperGrid)
        {
            lowerLink.UpperGrid = null;
            if (lowerLink.LowerGrid == null)
                RemComp<ApotheosisZLinkComponent>(lowerGrid);
        }

        RemComp<MapGridProjectionComponent>(upperGrid);
        RemComp<ApotheosisZLevelManagedComponent>(upperGrid);
        return true;
    }

    private void UpdatePvsSubscriptions()
    {
        _seenSessions.Clear();

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out var actor, out var transform))
        {
            var session = actor.PlayerSession;
            _seenSessions.Add(session);

            EntityUid? desiredSource = null;
            EntityUid? upperGrid = null;
            if (transform.GridUid is { } grid &&
                TryComp(grid, out MapGridProjectionComponent? projection) &&
                projection.SourceGrid is { } source &&
                Exists(source))
            {
                upperGrid = grid;
                desiredSource = source;
            }

            SetSessionSource(session, desiredSource);

            if (upperGrid is { } upper && desiredSource is { } lower)
                UpdateProjectedEntities(session, upper, lower, transform);
            else
                ClearProjectedEntities(session);
        }

        _removedSessions.Clear();
        foreach (var session in _sessionSources.Keys)
        {
            if (!_seenSessions.Contains(session))
                _removedSessions.Add(session);
        }

        foreach (var session in _removedSessions)
        {
            SetSessionSource(session, null);
        }
    }

    private void SetSessionSource(ICommonSession session, EntityUid? source)
    {
        if (_sessionSources.TryGetValue(session, out var oldSource))
        {
            if (oldSource == source)
                return;

            _pvs.RemoveForceSend(oldSource, session);
            _sessionSources.Remove(session);
            ClearProjectedEntities(session);
        }

        if (source is not { } newSource)
            return;

        _pvs.AddForceSend(newSource, session);
        _sessionSources[session] = newSource;
    }

    private void UpdateProjectedEntities(
        ICommonSession session,
        EntityUid upperGrid,
        EntityUid sourceGrid,
        TransformComponent actorTransform)
    {
        if (!TryComp(upperGrid, out MapGridComponent? upperGridComponent) ||
            !HasComp<MapGridComponent>(sourceGrid) ||
            !TryComp(upperGrid, out MapGridProjectionComponent? projection) ||
            !projection.RenderEntities)
        {
            ClearProjectedEntities(session);
            return;
        }

        var actorLocalPosition = _transform.GetRelativePosition(actorTransform, upperGrid);
        var localBounds = Box2.CenteredAround(
            actorLocalPosition,
            new Vector2(ProjectedEntityRange * 2f));
        var sourceWorldBounds = _transform.GetWorldMatrix(sourceGrid).TransformBox(localBounds);

        _projectedCandidates.Clear();
        _desiredProjectedEntities.Clear();
        _lookup.GetEntitiesIntersecting(
            sourceGrid,
            sourceWorldBounds,
            _projectedCandidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var uid in _projectedCandidates)
        {
            if (_desiredProjectedEntities.Count >= MaxProjectedEntitiesPerSession)
                break;

            if (uid == sourceGrid ||
                !TryComp(uid, out TransformComponent? transform) ||
                transform.GridUid != sourceGrid)
            {
                continue;
            }

            var localPosition = _transform.GetRelativePosition(transform, sourceGrid);
            if (!CanSeeLowerEntity(upperGrid, upperGridComponent, localPosition))
                continue;

            _desiredProjectedEntities.Add(uid);
        }

        if (!_sessionProjectedEntities.TryGetValue(session, out var current))
        {
            current = new HashSet<EntityUid>();
            _sessionProjectedEntities.Add(session, current);
        }

        _removedProjectedEntities.Clear();
        foreach (var uid in current)
        {
            if (!_desiredProjectedEntities.Contains(uid))
                _removedProjectedEntities.Add(uid);
        }

        foreach (var uid in _removedProjectedEntities)
        {
            _pvs.RemoveSessionDirectOverride(uid, session);
            current.Remove(uid);
        }

        foreach (var uid in _desiredProjectedEntities)
        {
            if (!current.Add(uid))
                continue;

            _pvs.AddSessionDirectOverride(uid, session);
        }
    }

    private bool CanSeeLowerEntity(
        EntityUid upperGrid,
        MapGridComponent upperGridComponent,
        Vector2 localPosition)
    {
        var coordinates = new EntityCoordinates(upperGrid, localPosition);
        var tile = _map.GetTileRef(upperGrid, upperGridComponent, coordinates).Tile;
        return tile.IsEmpty || _tiles[tile.TypeId].RenderZLevelBelow;
    }

    private void ClearProjectedEntities(ICommonSession session)
    {
        if (!_sessionProjectedEntities.Remove(session, out var entities))
            return;

        foreach (var uid in entities)
        {
            _pvs.RemoveSessionDirectOverride(uid, session);
        }
    }
}
