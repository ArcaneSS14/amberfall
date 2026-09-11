using Content.Shared.Maps;
using Robust.Shared.Map.Components;

namespace Content.Apotheosis.Shared.ZLevels;

/// <summary>
/// Tile rules shared by projection rendering and its server PVS subscriptions.
/// Nothing here requires extensions to the engine's tile interface.
/// </summary>
public sealed partial class ZLevelVisibilitySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    private List<Entity<MapGridComponent>> _grids = new();

    public bool IsOpening(Tile tile) =>
        tile.IsEmpty || _tiles[tile.TypeId] is ContentTileDefinition { RenderZLevelBelow: true };

    /// <summary>
    /// Builds cumulative apertures: every opaque floor above a view cuts it out.
    /// A budget-exhausted layer and everything below it are omitted, never exposed.
    /// </summary>
    public void BuildViews(EntityUid upperMap, Box2 bounds, List<ZLevelView> views)
    {
        views.Clear();
        var regions = new List<Vector2[]> { ZLevelApertures.Rectangle(bounds) };
        var visited = new HashSet<EntityUid> { upperMap };
        var operations = 0;
        var current = upperMap;
        for (var depth = 0; depth < ZLevelApertures.MaxDepth; depth++)
        {
            if (!TryComp(current, out MapComponent? upper) ||
                !TryComp(current, out ZLevelProjectionComponent? projection) ||
                projection.SourceMap is not { } source || !visited.Add(source) ||
                !TryComp(source, out MapComponent? lower))
                break;

            _grids.Clear();
            _map.FindGridsIntersecting(upper.MapId, bounds, ref _grids, approx: true, includeMap: true);
            foreach (var grid in _grids)
            {
                var matrix = _transform.GetWorldMatrix(grid.Owner);
                foreach (var rectangle in GetOpaqueRectangles(grid, bounds))
                {
                    var blocker = ZLevelApertures.Rectangle(rectangle);
                    for (var i = 0; i < blocker.Length; i++)
                        blocker[i] = Vector2.Transform(blocker[i], matrix);
                    if (!ZLevelApertures.Subtract(regions, blocker, ref operations) || regions.Count == 0)
                        return;
                }
            }

            bounds = ZLevelApertures.Bounds(regions);
            views.Add(new ZLevelView(source, lower.MapId, new List<Vector2[]>(regions), bounds,
                float.IsFinite(projection.BlurRadius) ? Math.Clamp(projection.BlurRadius, 0f, 8f) : 0f,
                projection.RenderEntities));
            current = source;
        }
    }

    /// <summary>Merge solid tile runs horizontally and vertically before clipping polygons.</summary>
    private IEnumerable<Box2> GetOpaqueRectangles(Entity<MapGridComponent> grid, Box2 bounds)
    {
        var indices = new List<Vector2i>();
        foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid.Comp, bounds))
        {
            if (!IsOpening(tile.Tile))
                indices.Add(tile.GridIndices);
        }
        indices.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var runs = new Dictionary<(int Left, int Right), (int Bottom, int Top)>();
        var size = grid.Comp.TileSize;
        for (var i = 0; i < indices.Count;)
        {
            var first = indices[i++];
            var right = first.X + 1;
            while (i < indices.Count && indices[i].Y == first.Y && indices[i].X == right)
            {
                right++;
                i++;
            }
            var key = (first.X, right);
            if (runs.TryGetValue(key, out var previous))
            {
                if (previous.Top == first.Y)
                {
                    runs[key] = (previous.Bottom, first.Y + 1);
                    continue;
                }
                yield return new Box2(first.X * size, previous.Bottom * size, right * size, previous.Top * size);
            }
            runs[key] = (first.Y, first.Y + 1);
        }
        foreach (var (run, rows) in runs)
            yield return new Box2(run.Left * size, rows.Bottom * size, run.Right * size, rows.Top * size);
    }

    /// <summary>
    /// Conservatively checks whether a region can expose the lower floor. A grid that
    /// completely covers the region with opaque tiles is sufficient to reject it.
    /// Rotated grids may yield false positives, never holes disappearing at camera edges.
    /// </summary>
    public bool HasOpening(MapId mapId, Box2 bounds)
    {
        _grids.Clear();
        _map.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true, includeMap: true);
        foreach (var grid in _grids)
        {
            var local = _transform.GetInvWorldMatrix(grid.Owner).TransformBox(bounds);
            var size = grid.Comp.TileSize;
            var left = (int) MathF.Floor(local.Left / size);
            var bottom = (int) MathF.Floor(local.Bottom / size);
            var right = (int) MathF.Ceiling(local.Right / size);
            var top = (int) MathF.Ceiling(local.Top / size);

            // Bound the work for extreme zoom levels or unusually small tiles.
            if ((long) (right - left) * (top - bottom) > 4096)
                continue;

            var covered = true;
            for (var x = left; x < right && covered; x++)
            for (var y = bottom; y < top; y++)
            {
                if (!IsOpening(_map.GetTileRef(grid, new Vector2i(x, y)).Tile))
                    continue;

                covered = false;
                break;
            }

            if (covered)
                return false;
        }

        return true;
    }
}
