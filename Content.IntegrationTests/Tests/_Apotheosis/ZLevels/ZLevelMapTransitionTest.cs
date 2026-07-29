using System.Numerics;
using Content.IntegrationTests.Fixtures;
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
}
