using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Amberfall;
using Content.Amberfall.Common.ZLevels;
using Content.Amberfall.Server.ZLevels;
using Content.Amberfall.Shared.ZLevels;
using Content.Shared.StepTrigger.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Amberfall.ZLevels;

[TestFixture]
public sealed class ZLevelMapTransitionTest : GameTest
{
    private const string PhysicsDummy = "ZLevelPhysicsDummy";

    [TestPrototypes]
    private const string TestPrototypes = $"""
- type: entity
  id: {PhysicsDummy}
  components:
    - type: Transform
    - type: Physics
      bodyType: Dynamic
""";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task LinksAndTransitionsDoNotRequireProjection()
    {
        await Server.WaitAssertion(() =>
        {
            var map = SEntMan.System<MapSystem>();
            var levels = SEntMan.System<ZLevelSystem>();
            var upper = map.CreateMap(out var upperId);
            var lower = map.CreateMap(out var lowerId);
            SEntMan.AddComponent(upper, new ZLevelComponent
            {
                Group = "no-projection", Level = 1, ProjectBelow = false,
            });
            SEntMan.AddComponent(lower, new ZLevelComponent
            {
                Group = "no-projection", Level = 0,
            });
            levels.Update(0f);
            Assert.That(SEntMan.HasComponent<ZLevelProjectionComponent>(upper), Is.False);
            Assert.That(SEntMan.GetComponent<ZLevelLinkComponent>(upper).LowerMap, Is.EqualTo(lower));

            var upperGrid = map.CreateGridEntity(upperId);
            var lowerGrid = map.CreateGridEntity(lowerId);
            map.SetTile(upperGrid, Vector2i.Zero, new Tile(1));
            map.SetTile(lowerGrid, new Vector2i(1, 0), new Tile(1));
            var falling = SEntMan.SpawnEntity(PhysicsDummy, new EntityCoordinates(upperGrid.Owner, 0.5f, 0.5f));
            SEntMan.System<TransformSystem>().SetCoordinates(falling, new EntityCoordinates(upperGrid.Owner, 1.5f, 0.5f));
            Assert.That(SEntMan.GetComponent<TransformComponent>(falling).MapUid, Is.EqualTo(lower));

            Assert.That(levels.LinkMaps(upper, lower, 3f, false), Is.True);
            Assert.That(SEntMan.GetComponent<ZLevelProjectionComponent>(upper).BlurRadius, Is.EqualTo(3f));
            Assert.That(SEntMan.GetComponent<ZLevelProjectionComponent>(upper).RenderEntities, Is.False);
            Assert.That(levels.UnlinkLower(upper), Is.True);
            Assert.That(SEntMan.HasComponent<ZLevelProjectionComponent>(upper), Is.False);
            Assert.That(SEntMan.HasComponent<ZLevelLinkComponent>(lower), Is.False);
            map.DeleteMap(upperId);
            map.DeleteMap(lowerId);
        });
    }

    [Test]
    public async Task ProjectionSleepsUnderSolidFloorsAndWakesForHoles()
    {
        await Server.WaitAssertion(() =>
        {
            var map = SEntMan.System<MapSystem>();
            var visibility = SEntMan.System<ZLevelVisibilitySystem>();
            var transform = SEntMan.System<TransformSystem>();
            map.CreateMap(out var mapId);
            var grid = map.CreateGridEntity(mapId);
            var bounds = Box2.FromDimensions(Vector2.Zero, new Vector2(2));
            Assert.That(visibility.HasOpening(mapId, bounds), Is.True, "Empty space must expose the lower floor.");
            for (var x = -3; x < 4; x++)
            for (var y = -3; y < 4; y++)
                map.SetTile(grid, new Vector2i(x, y), new Tile(1));

            Assert.That(visibility.HasOpening(mapId, bounds), Is.False);
            map.SetTile(grid, Vector2i.Zero, Tile.Empty);
            Assert.That(visibility.HasOpening(mapId, bounds), Is.True);
            var pit = Server.ResolveDependency<ITileDefinitionManager>()["FloorAmberfallTransparentPit"];
            map.SetTile(grid, Vector2i.Zero, new Tile(pit.TileId));
            Assert.That(visibility.HasOpening(mapId, bounds), Is.True, "A nonempty transparent pit is also an opening.");

            map.SetTile(grid, Vector2i.Zero, new Tile(1));
            transform.SetWorldRotation(grid.Owner, Angle.FromDegrees(45));
            Assert.That(visibility.HasOpening(mapId, bounds), Is.False, "Solid rotated grids can still skip projection.");
            map.SetTile(grid, Vector2i.Zero, Tile.Empty);
            Assert.That(visibility.HasOpening(mapId, bounds), Is.True, "Rotated holes must not be culled.");
            map.DeleteMap(mapId);
        });
    }

    [Test]
    public async Task AutomaticallyLinksMapLevelComponents()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var mapSystem = entityManager.System<MapSystem>();

        MapId upperMapId = default;
        MapId lowerMapId = default;
        EntityUid upperMap = default;
        EntityUid lowerMap = default;

        await server.WaitAssertion(() =>
        {
            upperMap = mapSystem.CreateMap(out upperMapId);
            lowerMap = mapSystem.CreateMap(out lowerMapId);

            entityManager.AddComponent(upperMap, new ZLevelComponent
            {
                Group = "integration-test",
                Level = 0,
            });
            entityManager.AddComponent(lowerMap, new ZLevelComponent
            {
                Group = "integration-test",
                Level = -1,
            });
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(
                entityManager.GetComponent<ZLevelProjectionComponent>(upperMap).SourceMap,
                Is.EqualTo(lowerMap));
        });

        await server.WaitPost(() =>
        {
            mapSystem.DeleteMap(upperMapId);
            mapSystem.DeleteMap(lowerMapId);
        });
    }

    [Test]
    public async Task TransitionsPhysicalEntitiesByMapCoordinates()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var mapSystem = entityManager.System<MapSystem>();
        var transformSystem = entityManager.System<TransformSystem>();
        var zLevelSystem = entityManager.System<ZLevelSystem>();

        MapId upperMapId = default;
        MapId lowerMapId = default;
        EntityUid upperMap = default;
        EntityUid lowerMap = default;
        EntityUid lowerGrid = default;
        EntityUid fallingEntity = default;

        await server.WaitAssertion(() =>
        {
            upperMap = mapSystem.CreateMap(out upperMapId);
            lowerMap = mapSystem.CreateMap(out lowerMapId);

            var upperGrid = mapSystem.CreateGridEntity(upperMapId);
            var lowerGridEntity = mapSystem.CreateGridEntity(lowerMapId);
            lowerGrid = lowerGridEntity.Owner;

            mapSystem.SetTile(upperGrid, new Vector2i(9, 0), new Tile(1));
            mapSystem.SetTile(lowerGridEntity, Vector2i.Zero, new Tile(1));
            transformSystem.SetWorldPosition(lowerGrid, new Vector2(10f, 0f));

            Assert.That(zLevelSystem.LinkMaps(upperMap, lowerMap), Is.True);
            Assert.That(
                entityManager.GetComponent<ZLevelProjectionComponent>(upperMap).SourceMap,
                Is.EqualTo(lowerMap));

            fallingEntity = entityManager.SpawnEntity(
                PhysicsDummy,
                new EntityCoordinates(upperGrid.Owner, 9.5f, 0.5f));

            transformSystem.SetCoordinates(
                fallingEntity,
                new EntityCoordinates(upperGrid.Owner, 10.5f, 0.5f));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var transform = entityManager.GetComponent<TransformComponent>(fallingEntity);
            var mapCoordinates = transformSystem.GetMapCoordinates(transform);

            Assert.Multiple(() =>
            {
                Assert.That(transform.MapID, Is.EqualTo(lowerMapId));
                Assert.That(transform.GridUid, Is.EqualTo(lowerGrid));
                Assert.That(mapCoordinates.Position.X, Is.EqualTo(10.5f).Within(0.001f));
                Assert.That(mapCoordinates.Position.Y, Is.EqualTo(0.5f).Within(0.001f));
            });
        });

        await server.WaitAssertion(() =>
        {
            var stairsEntity = entityManager.SpawnEntity(
                null,
                new EntityCoordinates(lowerGrid, 0.5f, 0.5f));
            entityManager.AddComponent(stairsEntity, new ZTransitionComponent
            {
                Direction = ZLevelDirection.Up,
                DestinationOffset = new Vector2(-1f, 0f),
            });

            var stairPassenger = entityManager.SpawnEntity(
                PhysicsDummy,
                new EntityCoordinates(lowerGrid, 0.5f, 0.5f));
            var ev = new StepTriggeredOffEvent(stairsEntity, stairPassenger);
            entityManager.EventBus.RaiseLocalEvent(stairsEntity, ref ev);

            var transform = entityManager.GetComponent<TransformComponent>(stairPassenger);
            var mapCoordinates = transformSystem.GetMapCoordinates(transform);
            Assert.Multiple(() =>
            {
                Assert.That(transform.MapID, Is.EqualTo(upperMapId));
                Assert.That(mapCoordinates.Position.X, Is.EqualTo(9.5f).Within(0.001f));
                Assert.That(mapCoordinates.Position.Y, Is.EqualTo(0.5f).Within(0.001f));
            });
        });

        await server.WaitPost(() =>
        {
            mapSystem.DeleteMap(upperMapId);
            mapSystem.DeleteMap(lowerMapId);
        });
    }

    [Test]
    public async Task TransparentPitFallsToLinkedLowerLevel()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var mapSystem = entityManager.System<MapSystem>();
        var transformSystem = entityManager.System<TransformSystem>();
        var zLevelSystem = entityManager.System<ZLevelSystem>();
        var tileDefinitions = server.ResolveDependency<ITileDefinitionManager>();

        MapId upperMapId = default;
        MapId lowerMapId = default;
        EntityUid fallingEntity = default;

        await server.WaitAssertion(() =>
        {
            var upperMap = mapSystem.CreateMap(out upperMapId);
            var lowerMap = mapSystem.CreateMap(out lowerMapId);
            var upperGrid = mapSystem.CreateGridEntity(upperMapId);
            var lowerGrid = mapSystem.CreateGridEntity(lowerMapId);
            var pitDefinition = (Content.Shared.Maps.ContentTileDefinition) tileDefinitions["FloorAmberfallTransparentPit"];

            Assert.Multiple(() =>
            {
                Assert.That(pitDefinition.RenderZLevelBelow, Is.True);
                Assert.That(pitDefinition.FallThroughZLevel, Is.True);
            });

            mapSystem.SetTile(upperGrid, new Vector2i(-1, 0), new Tile(1));
            mapSystem.SetTile(
                upperGrid,
                Vector2i.Zero,
                new Tile(pitDefinition.TileId));
            mapSystem.SetTile(lowerGrid, Vector2i.Zero, new Tile(1));

            Assert.That(zLevelSystem.LinkMaps(upperMap, lowerMap), Is.True);

            fallingEntity = entityManager.SpawnEntity(
                PhysicsDummy,
                new EntityCoordinates(upperGrid.Owner, -0.5f, 0.5f));
            transformSystem.SetCoordinates(
                fallingEntity,
                new EntityCoordinates(upperGrid.Owner, 0.5f, 0.5f));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var transform = entityManager.GetComponent<TransformComponent>(fallingEntity);
            Assert.That(transform.MapID, Is.EqualTo(lowerMapId));
        });

        await server.WaitPost(() =>
        {
            mapSystem.DeleteMap(upperMapId);
            mapSystem.DeleteMap(lowerMapId);
        });
    }

    [Test]
    public async Task TreeCanopiesFollowLateLinksAndRestoreOverlaps()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var mapSystem = entityManager.System<MapSystem>();
        var zLevelSystem = entityManager.System<ZLevelSystem>();

        MapId upperMapId = default;
        MapId lowerMapId = default;
        EntityUid upperMap = default;
        EntityUid lowerMap = default;
        EntityUid upperGrid = default;
        EntityUid lowerGrid = default;
        EntityUid firstTree = default;
        EntityUid secondTree = default;
        Tile originalTile = default;
        Tile canopyTile = default;

        await server.WaitAssertion(() =>
        {
            upperMap = mapSystem.CreateMap(out upperMapId);
            lowerMap = mapSystem.CreateMap(out lowerMapId);
            var upperGridEntity = mapSystem.CreateGridEntity(upperMapId);
            var lowerGridEntity = mapSystem.CreateGridEntity(lowerMapId);
            upperGrid = upperGridEntity.Owner;
            lowerGrid = lowerGridEntity.Owner;

            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    mapSystem.SetTile(upperGridEntity, new Vector2i(x, y), new Tile(1));
                    mapSystem.SetTile(lowerGridEntity, new Vector2i(x, y), new Tile(1));
                }
            }

            var upperGridComponent = entityManager.GetComponent<MapGridComponent>(upperGrid);
            originalTile = mapSystem.GetTileRef(upperGrid, upperGridComponent, new Vector2i(1, 1)).Tile;
            firstTree = entityManager.SpawnEntity(
                "AncientTree1",
                new EntityCoordinates(lowerGrid, 1.5f, 1.5f));

            Assert.That(entityManager.GetComponent<TreeCanopyComponent>(firstTree).SpawnedBranches, Is.Empty);
            Assert.That(zLevelSystem.LinkMaps(upperMap, lowerMap), Is.True);

            var firstCanopy = entityManager.GetComponent<TreeCanopyComponent>(firstTree);
            canopyTile = mapSystem.GetTileRef(upperGrid, upperGridComponent, new Vector2i(1, 1)).Tile;
            Assert.Multiple(() =>
            {
                Assert.That(firstCanopy.SpawnedBranches, Has.Count.EqualTo(10));
                Assert.That(firstCanopy.CanopyTiles, Has.Count.EqualTo(13));
                Assert.That(canopyTile, Is.Not.EqualTo(originalTile));
            });

            secondTree = entityManager.SpawnEntity(
                "AncientTree1",
                new EntityCoordinates(lowerGrid, 1.5f, 1.5f));
            Assert.That(entityManager.GetComponent<TreeCanopyComponent>(secondTree).SpawnedBranches, Has.Count.EqualTo(10));

            entityManager.DeleteEntity(firstTree);
            Assert.That(mapSystem.GetTileRef(upperGrid, upperGridComponent, new Vector2i(1, 1)).Tile, Is.EqualTo(canopyTile));

            entityManager.DeleteEntity(secondTree);
            Assert.That(mapSystem.GetTileRef(upperGrid, upperGridComponent, new Vector2i(1, 1)).Tile, Is.EqualTo(originalTile));
        });

        await server.WaitPost(() =>
        {
            mapSystem.DeleteMap(upperMapId);
            mapSystem.DeleteMap(lowerMapId);
        });
    }
}
