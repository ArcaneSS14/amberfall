using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

using Content.Amberfall.Shared.ZLevels;

namespace Content.Amberfall.Server.ZLevels;

public sealed partial class ZTransitionSystem
{
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private static readonly TimeSpan FallCooldown = TimeSpan.FromSeconds(0.1);
    private const int MaxLevelTraversal = 64;

    private void OnMove(ref MoveEvent args)
    {
        var uid = args.Entity.Owner;
        if (args.OnlyRotation ||
            args.Component.Anchored ||
            !TryComp(uid, out PhysicsComponent? physics) ||
            physics.BodyType == BodyType.Static ||
            !CanTransition(uid) ||
            args.Component.MapUid is not { } sourceMap ||
            !TryComp(sourceMap, out ZLevelLinkComponent? link) ||
            link.LowerMap is not { } firstLowerMap)
        {
            return;
        }

        var mapPosition = _transform.GetMapCoordinates(args.Component).Position;
        if (!CanFallThrough(sourceMap, mapPosition))
            return;

        if (!TryFindLanding(firstLowerMap, mapPosition, out var destinationMap, out var destinationParent))
            return;

        Transition(
            uid,
            destinationMap,
            destinationParent,
            mapPosition,
            FallCooldown,
            applyFallDamage: true);
    }

    private bool TryFindLanding(
        EntityUid firstLowerMap,
        Vector2 mapPosition,
        out EntityUid destinationMap,
        out EntityUid destinationParent)
    {
        destinationMap = default;
        destinationParent = default;
        var currentMap = firstLowerMap;

        for (var depth = 0; depth < MaxLevelTraversal; depth++)
        {
            if (!TryComp(currentMap, out MapComponent? mapComponent))
                return false;

            var mapCoordinates = new MapCoordinates(mapPosition, mapComponent.MapId);
            if (TryGetSurface(mapCoordinates, out var grid, out var tile) &&
                !CanFallThrough(tile))
            {
                destinationMap = currentMap;
                destinationParent = grid;
                return true;
            }

            if (!TryComp(currentMap, out ZLevelLinkComponent? link) ||
                link.LowerMap is not { } nextLowerMap)
            {
                destinationMap = currentMap;
                destinationParent = currentMap;
                return true;
            }

            currentMap = nextLowerMap;
        }

        return false;
    }

    private bool CanFallThrough(EntityUid mapUid, Vector2 mapPosition)
    {
        if (!TryComp(mapUid, out MapComponent? mapComponent))
            return false;

        var coordinates = new MapCoordinates(mapPosition, mapComponent.MapId);
        return !TryGetSurface(coordinates, out _, out var tile) || CanFallThrough(tile);
    }

    private bool TryGetSurface(MapCoordinates coordinates, out EntityUid gridUid, out Tile tile)
    {
        tile = default;
        if (!_map.TryFindGridAt(coordinates, out gridUid, out var grid))
            return false;

        var localCoordinates = _transform.ToCoordinates(gridUid, coordinates);
        tile = _map.GetTileRef(gridUid, grid, localCoordinates).Tile;
        return !tile.IsEmpty;
    }

    private bool CanFallThrough(Tile tile)
    {
        return tile.IsEmpty || (_tiles[tile.TypeId] is Content.Shared.Maps.ContentTileDefinition { FallThroughZLevel: true });
    }
}
