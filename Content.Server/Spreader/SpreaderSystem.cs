using Content.Shared.Maps;
using Content.Shared.Spreader;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Spreader;

/// <summary>
/// Handles generic spreading logic, where one anchored entity spreads to neighboring tiles.
/// </summary>
public sealed partial class SpreaderSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    [Dependency] private EntityQuery<EdgeSpreaderComponent> _edgeSpreaderQuery = default!;

    /// <summary>
    /// Cached maximum number of updates per spreader prototype. This is applied per-grid.
    /// </summary>
    private Dictionary<string, int> _prototypeUpdates = default!;

    /// <summary>
    /// Remaining number of updates per grid & prototype.
    /// </summary>
    // TODO PERFORMANCE Assign each prototype to an index and convert dictionary to array
    private readonly Dictionary<EntityUid, Dictionary<string, int>> _gridUpdates = [];

    public const float SpreadCooldownSeconds = 1;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypeReload);

        SubscribeLocalEvent<EdgeSpreaderComponent, EntityTerminatingEvent>(OnTerminating);
        SetupPrototypes();
    }

    private void OnPrototypeReload(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<EdgeSpreaderPrototype>())
            SetupPrototypes();
    }

    private void SetupPrototypes()
    {
        _prototypeUpdates = [];
        foreach (var proto in ProtoMan.EnumeratePrototypes<EdgeSpreaderPrototype>())
        {
            _prototypeUpdates.Add(proto.ID, proto.UpdatesPerSecond);
        }
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        EnsureComp<SpreaderGridComponent>(ev.EntityUid);
    }

    private void OnTerminating(Entity<EdgeSpreaderComponent> entity, ref EntityTerminatingEvent args)
    {
        ActivateSpreadableNeighbors(entity);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        // Check which grids are valid for spreading
        var spreadGrids = EntityQueryEnumerator<SpreaderGridComponent>();

        _gridUpdates.Clear();
        while (spreadGrids.MoveNext(out var uid, out var grid))
        {
            grid.UpdateAccumulator -= frameTime;
            if (grid.UpdateAccumulator > 0)
                continue;

            _gridUpdates[uid] = _prototypeUpdates.ShallowClone();
            grid.UpdateAccumulator += SpreadCooldownSeconds;
        }

        if (_gridUpdates.Count == 0)
            return;

        var query = EntityQueryEnumerator<ActiveEdgeSpreaderComponent>();
        var spreaders = new List<(EntityUid Uid, ActiveEdgeSpreaderComponent Comp)>(Count<ActiveEdgeSpreaderComponent>());

        // Build a list of all existing Edgespreaders, shuffle them
        while (query.MoveNext(out var uid, out var comp))
        {
            spreaders.Add((uid, comp));
        }

        _robustRandom.Shuffle(spreaders);

        // Remove the EdgeSpreaderComponent from any entity
        // that doesn't meet a few trivial prerequisites
        foreach (var (uid, comp) in spreaders)
        {
            // Get xform first, as entity may have been deleted due to interactions triggered by other spreaders.
            if (!TryComp(uid, out TransformComponent? xform))
                continue;

            if (xform.GridUid == null)
            {
                RemComp(uid, comp);
                continue;
            }

            if (!_gridUpdates.TryGetValue(xform.GridUid.Value, out var groupUpdates))
                continue;

            if (!_edgeSpreaderQuery.TryGetComponent(uid, out var spreader))
            {
                RemComp(uid, comp);
                continue;
            }

            if (!groupUpdates.TryGetValue(spreader.Id, out var updates) || updates < 1)
                continue;

            // Edge detection logic is to be handled
            // by the subscribing system, see KudzuSystem
            // for a simple example
            Spread(uid, xform, spreader.Id, ref updates);

            if (updates < 1)
                groupUpdates.Remove(spreader.Id);
            else
                groupUpdates[spreader.Id] = updates;
        }
    }

    private void Spread(EntityUid uid, TransformComponent xform, ProtoId<EdgeSpreaderPrototype> prototype, ref int updates)
    {
        GetNeighbors(uid, xform, prototype, out var freeTiles, out _, out var neighbors);

        var ev = new SpreadNeighborsEvent()
        {
            NeighborFreeTiles = freeTiles,
            Neighbors = neighbors,
            Updates = updates,
        };

        RaiseLocalEvent(uid, ref ev);
        updates = ev.Updates;
    }

    /// <summary>
    /// Gets the neighboring node data for the specified entity and the specified node group.
    /// </summary>
    public void GetNeighbors(EntityUid uid, TransformComponent comp, ProtoId<EdgeSpreaderPrototype> prototype, out ValueList<(MapGridComponent, TileRef)> freeTiles, out ValueList<Vector2i> occupiedTiles, out ValueList<EntityUid> neighbors)
    {
        freeTiles = [];
        occupiedTiles = [];
        neighbors = [];
        // TODO remove occupiedTiles -- its currently unused and just slows this method down.
        if (!ProtoMan.Resolve(prototype, out var spreaderPrototype))
            return;

        if (!TryComp<MapGridComponent>(comp.GridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(comp.GridUid.Value, grid, comp.Coordinates);
        var neighborTiles = new[]
        {
            tile + new Vector2i(0, 1),
            tile + new Vector2i(0, -1),
            tile + new Vector2i(1, 0),
            tile + new Vector2i(-1, 0),
        };

        foreach (var neighborPos in neighborTiles)
        {
            if (!_map.TryGetTileRef(comp.GridUid.Value, grid, neighborPos, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            if (spreaderPrototype.PreventSpreadOnSpaced && _turf.IsSpace(tileRef))
                continue;

            var directionEnumerator = _map.GetAnchoredEntitiesEnumerator(comp.GridUid.Value, grid, neighborPos);

            var oldCount = occupiedTiles.Count;
            // Goob edit end

            while (directionEnumerator.MoveNext(out var ent))
            {
                if (!_edgeSpreaderQuery.TryGetComponent(ent, out var spreader))
                    continue;

                if (spreader.Id != prototype)
                    continue;

                neighbors.Add(ent.Value);
                occupiedTiles.Add(neighborPos);
                break;
            }

            if (oldCount == occupiedTiles.Count)
                freeTiles.Add((grid, tileRef));
        }
    }

    /// <summary>
    /// This function activates all spreaders that are adjacent to a given entity. This also activates other spreaders
    /// on the same tile as the current entity (for thin airtight entities like windoors).
    /// </summary>
    public void ActivateSpreadableNeighbors(EntityUid origin, (EntityUid Grid, Vector2i Tile)? position = null)
    {
        Vector2i tile;
        EntityUid gridUid;
        MapGridComponent? gridComp;

        if (position == null)
        {
            var transform = Transform(origin);
            if (!TryComp(transform.GridUid, out gridComp) || TerminatingOrDeleted(transform.GridUid.Value))
                return;

            tile = _map.TileIndicesFor(transform.GridUid.Value, gridComp, transform.Coordinates);
            gridUid = transform.GridUid.Value;
        }
        else
        {
            if (!TryComp(position.Value.Grid, out gridComp))
                return;
            (gridUid, tile) = position.Value;
        }

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, gridComp, tile);
        while (anchored.MoveNext(out var entity))
        {
            // Don't re-activate the terminating entity
            if (entity == origin)
                continue;
            DebugTools.Assert(Transform(entity.Value).Anchored);

            // Activate any edge spreaders that are non-terminating
            if (_edgeSpreaderQuery.HasComponent(entity) && !TerminatingOrDeleted(entity))
                EnsureComp<ActiveEdgeSpreaderComponent>(entity.Value);
        }

        for (var i = 0; i < 4; i++)
        {
            var adjacentTile = i switch
            {
                0 => tile + new Vector2i(0, 1),
                1 => tile + new Vector2i(0, -1),
                2 => tile + new Vector2i(1, 0),
                _ => tile + new Vector2i(-1, 0),
            };
            anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, gridComp, adjacentTile);

            while (anchored.MoveNext(out var entity))
            {
                DebugTools.Assert(Transform(entity.Value).Anchored);

                // Activate any edge spreaders that are non-terminating
                if (_edgeSpreaderQuery.HasComponent(entity) && !TerminatingOrDeleted(entity))
                    EnsureComp<ActiveEdgeSpreaderComponent>(entity.Value);
            }
        }
    }

    public bool RequiresFloorToSpread(EntProtoId<EdgeSpreaderComponent> spreader)
    {
        if (!ProtoMan.Index(spreader).TryComp<EdgeSpreaderComponent>(out var spreaderComp, EntityManager.ComponentFactory))
            return false;

        return ProtoMan.Index(spreaderComp.Id).PreventSpreadOnSpaced;
    }
}
