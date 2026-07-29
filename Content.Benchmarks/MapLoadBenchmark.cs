using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Shared.Maps;
using Robust.Shared;
using Robust.Shared.Analyzers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Benchmarks;

[Virtual]
public class MapLoadBenchmark
{
    private TestPair _pair = default!;
    private MapLoaderSystem _mapLoader = default!;
    private SharedMapSystem _mapSys = default!;

    [GlobalSetup]
    public void Setup()
    {
        ProgramShared.PathOffset = "../../../../";
        PoolManager.Startup();

        _pair = PoolManager.GetServerClient(testContext: new ExternalTestContext("Benchmark", StreamWriter.Null)).GetAwaiter().GetResult();
        var server = _pair.Server;

        Paths = server.ResolveDependency<IPrototypeManager>()
            .EnumeratePrototypes<GameMapPrototype>()
            .ToDictionary(x => x.ID, x => x.MapLayers.ToArray());

        _mapLoader = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<MapLoaderSystem>();
        _mapSys = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedMapSystem>();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pair.DisposeAsync();
        PoolManager.Shutdown();
    }

    public static string[] MapsSource { get; } = { "Empty", "Saltern", "Box", "Bagel", "Dev", "CentComm", "Atlas", "Core", "TestTeg", "Packed", "Origin", "Omega", "Cluster", "Reach", "Meta", "Marathon", "Nume", "MeteorArena", "Fland", "Oasis", "Barratry", "Kettle", "Submarine", "Lambda", "Leonid", "Delta", "Amber", "Chloris", "Cog", "Serpentcrest"}; // Trauma - completely changed

    [ParamsSource(nameof(MapsSource))]
    public string Map;

    public Dictionary<string, ResPath[]> Paths;
    private readonly List<MapId> _mapIds = new();

    [Benchmark]
    public async Task LoadMap()
    {
        var server = _pair.Server;
        await server.WaitPost(() =>
        {
            _mapIds.Clear();
            foreach (var mapPath in Paths[Map])
            {
                var success = _mapLoader.TryLoadMap(mapPath, out var map, out _);
                if (!success)
                    throw new Exception($"Map layer {mapPath} failed to load");

                _mapIds.Add(map.Value.Comp.MapId);
            }
        });
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var server = _pair.Server;
        server.WaitPost(() =>
            {
                foreach (var mapId in _mapIds)
                {
                    _mapSys.DeleteMap(mapId);
                }

                _mapIds.Clear();
            })
            .Wait();
    }
}
