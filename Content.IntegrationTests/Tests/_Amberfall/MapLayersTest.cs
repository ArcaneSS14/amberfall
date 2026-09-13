using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Amberfall;

[TestFixture]
public sealed class MapLayersTest : GameTest
{
    private const string TestMapId = "AmberfallMapLayersTest";

    [TestPrototypes]
    private const string TestPrototypes = $$"""
- type: gameMap
  id: {{TestMapId}}
  mapName: Amberfall Map Layers Test
  minPlayers: 0
  mapLayers:
    - /Maps/Test/empty.yml
    - /Maps/_Goobstation/Nonstations/dm01-entryway.yml
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components: []
    dm01-entryway:
      stationProto: StandardNanotrasenStation
      components: []
""";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task LoadsEveryLayerOnSeparateMap()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var prototypeManager = server.ProtoMan;
        var ticker = entityManager.System<GameTicker>();
        var mapSystem = entityManager.System<MapSystem>();
        var loadedMapIds = new HashSet<MapId>();

        await server.WaitAssertion(() =>
        {
            var prototype = prototypeManager.Index<GameMapPrototype>(TestMapId);
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            var grids = ticker.LoadGameMap(prototype, out var primaryMapId, options);

            Assert.That(grids, Has.Count.EqualTo(2));

            foreach (var grid in grids)
                loadedMapIds.Add(entityManager.GetComponent<TransformComponent>(grid).MapID);

            Assert.Multiple(() =>
            {
                Assert.That(loadedMapIds, Has.Count.EqualTo(2));
                Assert.That(loadedMapIds, Does.Contain(primaryMapId));
            });
        });

        await server.WaitPost(() =>
        {
            foreach (var mapId in loadedMapIds)
                mapSystem.DeleteMap(mapId);
        });
    }
}
