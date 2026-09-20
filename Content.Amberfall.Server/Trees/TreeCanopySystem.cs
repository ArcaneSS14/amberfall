using Content.Amberfall.Server.ZLevels;
using Content.Amberfall.Shared.ZLevels;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

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
        var query = EntityQueryEnumerator<TreeCanopyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var canopy, out var transform))
        {
            if (transform.MapUid == args.LowerMap)
                RefreshCanopy((uid, canopy));
        }
    }

    private void RefreshCanopy(Entity<TreeCanopyComponent> tree)
    {
        ClearCanopy(tree);

        var treeTransform = Transform(tree);
        if (treeTransform.MapUid is not { } sourceMap ||
            !TryComp(sourceMap, out ZLevelLinkComponent? link) ||
            link.UpperMap is not { } upperMap ||
            !TryComp(upperMap, out MapComponent? upperMapComponent))
        {
            return;
        }

        var mapPosition = _transform.GetMapCoordinates(treeTransform).Position;
        var upperCoordinates = new MapCoordinates(mapPosition, upperMapComponent.MapId);
        if (!_map.TryFindGridAt(upperCoordinates, out var upperGrid, out var upperGridComponent))
            return;

        var origin = _transform.ToCoordinates(upperGrid, upperCoordinates);
        ReplaceFoliageTiles(tree, upperGrid, upperGridComponent, origin);

        foreach (var direction in BranchDirections)
        {
            SpawnBranch(tree, tree.Comp.ExtendPrototype, origin, direction, tree.Comp.ExtendDistance);
            SpawnBranch(tree, tree.Comp.EndPrototype, origin, direction, tree.Comp.EndDistance);
        }

        SpawnBranch(tree, tree.Comp.ExtendPrototype, origin, Direction.North, 0);
        SpawnBranch(tree, tree.Comp.ExtendPrototype, origin, Direction.East, 0);
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

    private void SpawnBranch(
        Entity<TreeCanopyComponent> tree,
        EntProtoId prototype,
        EntityCoordinates origin,
        Direction direction,
        float distance)
    {
        var coordinates = origin.Offset(direction.ToAngle().ToWorldVec() * distance);
        var branch = Spawn(prototype, coordinates);
        _transform.SetLocalRotation(branch, direction.ToAngle());
        tree.Comp.SpawnedBranches.Add(branch);
    }

    private void OnShutdown(Entity<TreeCanopyComponent> ent, ref ComponentShutdown args)
    {
        ClearCanopy(ent);
    }

    private void ClearCanopy(Entity<TreeCanopyComponent> tree)
    {
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
