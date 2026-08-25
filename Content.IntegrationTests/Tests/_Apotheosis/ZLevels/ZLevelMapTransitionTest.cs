using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Apotheosis;
using Content.Apotheosis.Common.ZLevels;
using Content.Apotheosis.Server.ZLevels;
using Content.Apotheosis.Shared.ZLevels;
using Content.Shared.StepTrigger.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Apotheosis.ZLevels;

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
                entityManager.GetComponent<MapProjectionComponent>(upperMap).SourceMap,
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
                entityManager.GetComponent<MapProjectionComponent>(upperMap).SourceMap,
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
            var pitDefinition = tileDefinitions["FloorApotheosisTransparentPit"];

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
                Assert.That(firstCanopy.SpawnedBranches, Has.Count.EqualTo(8));
                Assert.That(firstCanopy.CanopyTiles, Has.Count.EqualTo(9));
                Assert.That(canopyTile, Is.Not.EqualTo(originalTile));
            });

            secondTree = entityManager.SpawnEntity(
                "AncientTree1",
                new EntityCoordinates(lowerGrid, 1.5f, 1.5f));
            Assert.That(entityManager.GetComponent<TreeCanopyComponent>(secondTree).SpawnedBranches, Has.Count.EqualTo(8));

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
