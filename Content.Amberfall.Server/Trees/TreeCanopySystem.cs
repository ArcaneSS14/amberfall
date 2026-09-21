using Content.Amberfall.Server.ZLevels;
using Content.Amberfall.Shared.ZLevels;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Amberfall;

public sealed partial class TreeCanopySystem : EntitySystem
{
    private const int FoliageRadius = 2;

    private static readonly Direction[] BranchDirections =
    [
        Direction.South,
        Direction.East,
        Direction.North,
        Direction.West,
    ];

    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<(EntityUid Grid, Vector2i Indices), CanopyTileState> _canopyTiles = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TreeCanopyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TreeCanopyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ZLevelLinkChangedEvent>(OnZLevelLinkChanged);
    }

    private void OnMapInit(Entity<TreeCanopyComponent> ent, ref MapInitEvent args)
    {
        RefreshCanopy(ent);
    }

    private void OnZLevelLinkChanged(ref ZLevelLinkChangedEvent args)
    {
        var trees = new List<EntityUid>();
        var query = EntityQueryEnumerator<TreeCanopyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.MapUid == args.LowerMap)
                trees.Add(uid);
        }

        foreach (var uid in trees)
        {
            if (TryComp(uid, out TreeCanopyComponent? canopy))
                RefreshCanopy((uid, canopy));
        }
    }

    private void RefreshCanopy(Entity<TreeCanopyComponent> tree)
    {
        var treeTransform = Transform(tree);
        var upperMap = treeTransform.MapUid is { } sourceMap &&
                       TryComp(sourceMap, out ZLevelLinkComponent? currentLink)
            ? currentLink.UpperMap
            : null;

        if (tree.Comp.TopTree is { } currentTop &&
            !TerminatingOrDeleted(currentTop) &&
            Transform(currentTop).MapUid == upperMap)
            return;

        if (upperMap == null &&
            tree.Comp.TopTree == null &&
            tree.Comp.SpawnedBranches.Count == 0 &&
            tree.Comp.CanopyTiles.Count == 0)
            return;

        ClearCanopy(tree);

        if (upperMap is not { } targetMap ||
            !TryComp(targetMap, out MapComponent? upperMapComponent))
        {
            return;
        }

        var mapPosition = _transform.GetMapCoordinates(treeTransform).Position;
        var upperCoordinates = new MapCoordinates(mapPosition, upperMapComponent.MapId);
        if (!_map.TryFindGridAt(upperCoordinates, out var upperGrid, out var upperGridComponent))
            return;

        var origin = _transform.ToCoordinates(upperGrid, upperCoordinates);
        ReplaceFoliageTiles(tree, upperGrid, upperGridComponent, origin);

        if (TryPrototype(tree.Owner, out var treePrototype))
        {
            var topTree = EntityManager.CreateEntityUninitialized(treePrototype.ID, origin);
            RemComp<TreeCanopyComponent>(topTree);
            tree.Comp.TopTree = topTree;
            EntityManager.InitializeAndStartEntity(topTree);
        }

        foreach (var direction in BranchDirections)
        {
            SpawnCanopy(tree, origin, direction);
        }
    }

    private void ReplaceFoliageTiles(
        Entity<TreeCanopyComponent> tree,
        EntityUid gridUid,
        MapGridComponent grid,
        EntityCoordinates origin)
    {
        var foliage = _tiles[tree.Comp.FoliageTile];
        var foliageTile = new Tile(foliage.TileId);
        var center = _map.GetTileRef(gridUid, grid, origin).GridIndices;

        for (var x = -FoliageRadius; x <= FoliageRadius; x++)
        {
            for (var y = -FoliageRadius; y <= FoliageRadius; y++)
            {
                if (x * x + y * y > FoliageRadius * FoliageRadius)
                    continue;

                var indices = center + new Vector2i(x, y);
                var key = (gridUid, indices);

                if (!_canopyTiles.TryGetValue(key, out var state))
                {
                    state = new CanopyTileState(_map.GetTileRef(gridUid, grid, indices).Tile);
                    _canopyTiles.Add(key, state);
                }

                state.Layers.Add((tree.Owner, foliageTile));
                tree.Comp.CanopyTiles.Add(key);
                _map.SetTile(gridUid, grid, indices, foliageTile);
            }
        }
    }

    private void SpawnCanopy(
        Entity<TreeCanopyComponent> tree,
        EntityCoordinates origin,
        Direction direction)
    {
        var canopy = tree.Comp;
        var isDouble = _random.Prob(0.7f);
        var coordinates = origin.Offset(direction.ToAngle().ToWorldVec());

        SpawnBranch(isDouble ? canopy.ExtendPrototype : canopy.EndPrototype, coordinates, direction, canopy);
        if (isDouble)
            SpawnBranch(canopy.EndPrototype, origin.Offset(direction.ToAngle().ToWorldVec() * 2), direction, canopy);
    }

    private EntityUid SpawnBranch(
        EntProtoId prototype,
        EntityCoordinates coordinates,
        Direction direction,
        TreeCanopyComponent canopy)
    {
        var branch = Spawn(prototype, coordinates);
        _transform.SetLocalRotation(branch, direction.ToAngle());

        canopy.SpawnedBranches.Add(branch);

        return branch;
    }

    private void OnShutdown(Entity<TreeCanopyComponent> ent, ref ComponentShutdown args)
    {
        ClearCanopy(ent);
    }

    private void ClearCanopy(Entity<TreeCanopyComponent> tree)
    {
        if (tree.Comp.TopTree is { } topTree && !TerminatingOrDeleted(topTree))
            QueueDel(topTree);

        tree.Comp.TopTree = null;

        foreach (var branch in tree.Comp.SpawnedBranches)
        {
            if (!TerminatingOrDeleted(branch))
                QueueDel(branch);
        }

        tree.Comp.SpawnedBranches.Clear();

        foreach (var key in tree.Comp.CanopyTiles)
        {
            if (!_canopyTiles.TryGetValue(key, out var state))
                continue;

            var index = state.Layers.FindLastIndex(layer => layer.Owner == tree.Owner);
            if (index < 0)
                continue;

            var wasTopLayer = index == state.Layers.Count - 1;
            state.Layers.RemoveAt(index);

            if (wasTopLayer &&
                !TerminatingOrDeleted(key.Grid) &&
                TryComp(key.Grid, out MapGridComponent? grid))
            {
                var tile = state.Layers.Count > 0
                    ? state.Layers[^1].Tile
                    : state.Original;
                _map.SetTile(key.Grid, grid, key.Indices, tile);
            }

            if (state.Layers.Count == 0)
                _canopyTiles.Remove(key);
        }

        tree.Comp.CanopyTiles.Clear();
    }

    private sealed class CanopyTileState(Tile original)
    {
        public readonly Tile Original = original;
        public readonly List<(EntityUid Owner, Tile Tile)> Layers = new();
    }
}
