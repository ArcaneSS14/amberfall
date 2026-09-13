using System.Numerics;
using Content.Amberfall.Shared.ZLevels;
using Content.Amberfall.Server.ZLevels;
using Content.IntegrationTests.Fixtures;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Amberfall.ZLevels;

[TestFixture]
public sealed class ZLevelProjectionNetworkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ClosingIntermediateFloorRemovesDeeperEntitiesFromPvs()
    {
        EntityUid top = default;
        EntityUid middle = default;
        EntityUid bottom = default;
        EntityUid observer = default;
        Entity<MapGridComponent> middleGrid = default;
        NetEntity targetNet = default;
        await Server.WaitAssertion(() =>
        {
            // The shared test pool normally disables PVS and sends every entity.
            Server.CfgMan.SetCVar(Robust.Shared.CVars.NetPVS, true);
            var map = SEntMan.System<MapSystem>();
            top = map.CreateMap(out var topId);
            middle = map.CreateMap(out var middleId);
            bottom = map.CreateMap(out var bottomId);
            var topGrid = map.CreateGridEntity(topId);
            middleGrid = map.CreateGridEntity(middleId);
            var bottomGrid = map.CreateGridEntity(bottomId);
            for (var x = -16; x <= 16; x++)
            for (var y = -16; y <= 16; y++)
            {
                if (x == 0 && y == 0)
                    continue;
                map.SetTile(topGrid, new Vector2i(x, y), new Tile(1));
                map.SetTile(middleGrid, new Vector2i(x, y), new Tile(1));
            }
            map.SetTile(bottomGrid, Vector2i.Zero, new Tile(1));
            var links = SEntMan.System<ZLevelSystem>();
            Assert.That(links.LinkMaps(top, middle), Is.True);
            Assert.That(links.LinkMaps(middle, bottom), Is.True);
            observer = SEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(0.5f), topId));
            SEntMan.AddComponent<EyeComponent>(observer);
            Server.PlayerMan.SetAttachedEntity(ServerSession, observer);
            var target = SEntMan.SpawnEntity(null, new EntityCoordinates(bottomGrid.Owner, new Vector2(0.5f)));
            targetNet = SEntMan.GetNetEntity(target);
        });

        await Pair.RunTicksSync(40);
        await Client.WaitAssertion(() =>
        {
            var target = CEntMan.GetEntity(targetNet);
            Assert.That(CEntMan.GetComponent<MetaDataComponent>(target).Flags.HasFlag(MetaDataFlags.Detached), Is.False);
            Assert.That(CEntMan.GetComponent<TransformComponent>(target).MapID, Is.Not.EqualTo(MapId.Nullspace));
        });
        await Server.WaitPost(() => SEntMan.System<MapSystem>().SetTile(middleGrid, Vector2i.Zero, new Tile(1)));
        await Pair.RunTicksSync(40);
        await Client.WaitAssertion(() =>
        {
            var target = CEntMan.GetEntity(targetNet);
            Assert.That(CEntMan.GetComponent<MetaDataComponent>(target).Flags.HasFlag(MetaDataFlags.Detached), Is.True);
        });
        await Server.WaitPost(() =>
        {
            Server.PlayerMan.SetAttachedEntity(ServerSession, null);
            SEntMan.DeleteEntity(top);
            SEntMan.DeleteEntity(middle);
            SEntMan.DeleteEntity(bottom);
            Server.CfgMan.SetCVar(Robust.Shared.CVars.NetPVS, false);
        });
    }

    [Test]
    public async Task ProjectionStateAndUnlinkReachTheStockClient()
    {
        EntityUid upper = default;
        EntityUid lower = default;
        NetEntity upperNet = default;
        NetEntity lowerNet = default;
        await Server.WaitAssertion(() =>
        {
            var map = SEntMan.System<MapSystem>();
            upper = map.CreateMap();
            lower = map.CreateMap();
            upperNet = SEntMan.GetNetEntity(upper);
            lowerNet = SEntMan.GetNetEntity(lower);
            Assert.That(SEntMan.System<ZLevelSystem>().LinkMaps(upper, lower, 2f, false), Is.True);
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var projection = CEntMan.GetComponent<ZLevelProjectionComponent>(CEntMan.GetEntity(upperNet));
            Assert.That(projection.SourceMap, Is.EqualTo(CEntMan.GetEntity(lowerNet)));
            Assert.That(projection.BlurRadius, Is.EqualTo(2f));
            Assert.That(projection.RenderEntities, Is.False);
        });

        await Server.WaitAssertion(() => Assert.That(SEntMan.System<ZLevelSystem>().UnlinkLower(upper), Is.True));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<ZLevelProjectionComponent>(CEntMan.GetEntity(upperNet)), Is.False);
            Assert.That(CEntMan.HasComponent<ZLevelLinkComponent>(CEntMan.GetEntity(lowerNet)), Is.False);
        });

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(upper);
            SEntMan.DeleteEntity(lower);
        });
    }
}
