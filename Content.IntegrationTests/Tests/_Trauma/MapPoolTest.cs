// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server.Maps;
using Content.Shared.Maps;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using System.Collections.Generic;

namespace Content.IntegrationTests.Tests._Trauma;

/// <summary>
/// Checks that every map in a pool has the required areas in the map prototype.
/// This means it was mapped at least once, maybe it was removed but that seems rare for important areas.
/// Also checks that there is at least 1 entity mapped for required entities.
/// </summary>
[Category("MapTests")]
public sealed class MapPoolTest : GameTest
{
    [Test]
    public async Task RequiredAreasMappedTest()
    {
        var pair = Pair;
        var server = pair.Server;

        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var deps = entMan.EntitySysManager.DependencyCollection;
        var loader = entMan.System<MapLoaderSystem>();

        var options = DeserializationOptions.Default;
        await server.WaitAssertion(() =>
        {
            var ev = new BeforeEntityReadEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);

            Assert.Multiple(() =>
            {
                var missingAreas = new HashSet<EntProtoId>();
                var missingEnts = new HashSet<EntProtoId>();
                foreach (var pool in proto.EnumeratePrototypes<GameMapPoolPrototype>())
                {
                    var requiredAreas = pool.RequiredAreas;
                    var requiredEnts = pool.RequiredEntities;
                    if (requiredAreas.Count == 0 && requiredEnts.Count == 0)
                        continue; // don't load anything for pools that dont need to be checked

                    foreach (var mapId in pool.Maps)
                    {
                        var mapProto = proto.Index<GameMapPrototype>(mapId);
                        missingAreas.Clear();
                        missingAreas.UnionWith(requiredAreas);

                        missingEnts.Clear();
                        missingEnts.UnionWith(requiredEnts);
                        missingEnts.ExceptWith(mapProto.IgnoredRequiredEntities);

                        foreach (var map in mapProto.MapLayers)
                        {
                            if (!loader.TryReadFile(map, out var data))
                            {
                                Assert.Fail($"Failed to read {map}");
                                continue;
                            }

                            // parses the yml but doesn't spawn anything, not terribly slow
                            var reader = new EntityDeserializer(deps,
                                data,
                                options,
                                ev.RenamedPrototypes,
                                ev.DeletedPrototypes);

                            if (!reader.TryProcessData())
                            {
                                Assert.Fail($"Failed to process {map}");
                                continue;
                            }

                            // check that at least 1 grid maybe uses each required area
                            foreach (var gridId in reader.GridYamlIds)
                            {
                                var grid = reader.YamlEntities[gridId].Components;
                                if (!grid.TryGetValue("AreaGrid", out var comp))
                                    continue; // outdated map?

                                if (!comp.TryGet<MappingDataNode>("areaMap", out var areaMap))
                                    continue; // no areas

                                foreach (var node in areaMap.Values)
                                {
                                    var area = ((ValueDataNode) node).Value;
                                    missingAreas.Remove(area);
                                }
                            }

                            missingEnts.RemoveWhere(id => reader.Prototypes.ContainsKey(id));
                        }

                        Assert.That(missingAreas, Is.Empty,
                            $"Map {mapId} ({string.Join(", ", mapProto.MapLayers)}) was missing these areas required by the pool: {string.Join(", ", missingAreas)}");

                        Assert.That(missingEnts, Is.Empty,
                            $"Map {mapId} ({string.Join(", ", mapProto.MapLayers)}) was missing these entities required by the pool: {string.Join(", ", missingEnts)}");
                    }
                }
            });
        });
    }
}
