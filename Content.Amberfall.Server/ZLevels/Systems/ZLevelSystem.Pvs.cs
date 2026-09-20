using System.Numerics;
using Content.Amberfall.Shared.ZLevels;
using Robust.Server.GameStates;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Amberfall.Server.ZLevels;

public sealed partial class ZLevelSystem
{
    private const float PvsUpdateInterval = 0.5f;
    private const float ProjectedEntityRange = 14f;
    private const float ProjectionSupportMargin = 2f;
    private const int MaxProjectedEntitiesPerSession = 3000;

    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ZLevelVisibilitySystem _visibility = default!;

    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionProjectionSources = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionProjectedEntities = new();
    private readonly HashSet<ICommonSession> _seenSessions = new();
    private readonly HashSet<ICommonSession> _removedSessions = new();
    private readonly HashSet<EntityUid> _projectedCandidates = new();
    private readonly HashSet<EntityUid> _desiredProjectedEntities = new();
    private readonly HashSet<EntityUid> _desiredProjectionSources = new();
    private readonly List<ZLevelView> _projectionViews = new();
    private readonly List<EntityUid> _removedEntities = new();

    private List<Entity<MapGridComponent>> _visibleProjectionGrids = new();
    private float _pvsAccumulator;

    private void InitializePvs()
    {
        SubscribeLocalEvent<ZLevelTransitionedEvent>(OnZLevelTransitioned);
        SubscribeLocalEvent<ZLevelLinkChangedEvent>(OnProjectionLinkChanged);
    }

    private void OnZLevelTransitioned(ZLevelTransitionedEvent args)
    {
        _pvsAccumulator = PvsUpdateInterval;
    }

    private void OnProjectionLinkChanged(ref ZLevelLinkChangedEvent args)
    {
        _pvsAccumulator = PvsUpdateInterval;
    }

    private void ShutdownPvs()
    {
        foreach (var (session, sources) in _sessionProjectionSources)
        {
            foreach (var uid in sources)
            {
                _pvs.RemoveForceSend(uid, session);
            }
        }

        foreach (var (session, entities) in _sessionProjectedEntities)
        {
            foreach (var uid in entities)
            {
                _pvs.RemoveSessionOverride(uid, session);
            }
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
            UpdateSessionSubscriptions(session, transform);
        }

        RemoveDisconnectedSessions();
    }

    private void UpdateSessionSubscriptions(ICommonSession session, TransformComponent transform)
    {
        _desiredProjectionSources.Clear();
        _desiredProjectedEntities.Clear();

        if (transform.MapUid is { } upperMap && HasComp<ZLevelProjectionComponent>(upperMap))
        {
            var position = _transform.GetMapCoordinates(transform).Position;
            var bounds = Box2.CenteredAround(position, new Vector2(ProjectedEntityRange * 2f));

            _visibility.BuildViews(upperMap, bounds, _projectionViews);
            foreach (var view in _projectionViews)
            {
                CollectProjection(view);
            }
        }

        SyncOverrides(session, _sessionProjectionSources, _desiredProjectionSources, forceSend: true);
        SyncOverrides(session, _sessionProjectedEntities, _desiredProjectedEntities, forceSend: false);
    }

    private void RemoveDisconnectedSessions()
    {
        _removedSessions.Clear();

        foreach (var session in _sessionProjectionSources.Keys)
        {
            if (!_seenSessions.Contains(session))
                _removedSessions.Add(session);
        }

        foreach (var session in _sessionProjectedEntities.Keys)
        {
            if (!_seenSessions.Contains(session))
                _removedSessions.Add(session);
        }

        _desiredProjectionSources.Clear();
        _desiredProjectedEntities.Clear();

        foreach (var session in _removedSessions)
        {
            SyncOverrides(session, _sessionProjectionSources, _desiredProjectionSources, forceSend: true);
            SyncOverrides(session, _sessionProjectedEntities, _desiredProjectedEntities, forceSend: false);
        }
    }

    private void CollectProjection(ZLevelView view)
    {
        var bounds = view.Bounds.Enlarged(ProjectionSupportMargin);
        _desiredProjectionSources.Add(view.SourceMap);
        _visibleProjectionGrids.Clear();
        _map.FindGridsIntersecting(view.MapId, bounds, ref _visibleProjectionGrids, approx: true, includeMap: false);

        foreach (var grid in _visibleProjectionGrids)
        {
            _desiredProjectionSources.Add(grid.Owner);
        }

        if (!view.RenderEntities || _desiredProjectedEntities.Count >= MaxProjectedEntitiesPerSession)
            return;

        _projectedCandidates.Clear();
        _lookup.GetEntitiesIntersecting(view.MapId, bounds, _projectedCandidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var uid in _projectedCandidates)
        {
            if (_desiredProjectedEntities.Count >= MaxProjectedEntitiesPerSession)
                break;

            if (uid == view.SourceMap || HasComp<MapGridComponent>(uid) ||
                !TryComp(uid, out TransformComponent? transform) || transform.MapUid != view.SourceMap)
            {
                continue;
            }

            var position = _transform.GetMapCoordinates(transform).Position;
            if (ZLevelApertures.IsNear(view.Apertures, position, ProjectionSupportMargin, view.ApertureBounds))
                _desiredProjectedEntities.Add(uid);
        }
    }

    private void SyncOverrides(
        ICommonSession session,
        Dictionary<ICommonSession, HashSet<EntityUid>> subscriptions,
        HashSet<EntityUid> desired,
        bool forceSend)
    {
        if (!subscriptions.TryGetValue(session, out var current))
        {
            if (desired.Count == 0)
                return;
            current = new HashSet<EntityUid>();
            subscriptions.Add(session, current);
        }

        _removedEntities.Clear();
        foreach (var uid in current)
        {
            if (!desired.Contains(uid))
                _removedEntities.Add(uid);
        }

        foreach (var uid in _removedEntities)
        {
            if (forceSend)
                _pvs.RemoveForceSend(uid, session);
            else
                _pvs.RemoveSessionOverride(uid, session);

            current.Remove(uid);
        }

        foreach (var uid in desired)
        {
            if (!current.Add(uid))
                continue;

            if (forceSend)
                _pvs.AddForceSend(uid, session);
            else
                _pvs.AddSessionOverride(uid, session);
        }

        if (current.Count == 0)
            subscriptions.Remove(session);
    }
}
