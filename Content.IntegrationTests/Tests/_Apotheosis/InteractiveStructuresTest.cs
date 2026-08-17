using System.Collections.Generic;
using Content.Apotheosis;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Atmos.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._Apotheosis;

[TestFixture]
public sealed class InteractiveStructuresTest : GameTest
{
    private const string FuelPrototype = "ApotheosisTestFuel";
    private const string NonFuelPrototype = "ApotheosisTestNonFuel";
    private const string IgniterPrototype = "ApotheosisTestIgniter";

    [TestPrototypes]
    private const string TestPrototypes = $$"""
- type: entity
  id: {{FuelPrototype}}
  components:
  - type: Transform
  - type: FireFuel
    amount: 37

- type: entity
  id: {{NonFuelPrototype}}
  components:
  - type: Transform
  - type: Flammable
    damage: {}

- type: entity
  id: {{IgniterPrototype}}
  components:
  - type: Transform
  - type: AlwaysHot
""";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task OpenWindowUpdatesCollisionVisionAirAndClimbing()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid window = default;

        await server.WaitAssertion(() =>
        {
            window = entityManager.SpawnEntity("DecorativeWindowWood", map.GridCoords);
            AssertWindowState(entityManager, window, false);

            var activate = new ActivateInWorldEvent(window, window, true);
            entityManager.EventBus.RaiseLocalEvent(window, activate);
            Assert.That(activate.Handled, Is.True);
            AssertWindowState(entityManager, window, true);

            activate = new ActivateInWorldEvent(window, window, true);
            entityManager.EventBus.RaiseLocalEvent(window, activate);
            AssertWindowState(entityManager, window, false);
        });
    }

    [Test]
    public async Task FireUsesFuelValueFromConsumedEntityBeforeIgnition()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fire = entityManager.SpawnEntity("RusticStoneFire", map.GridCoords);
            var user = entityManager.SpawnEntity(null, map.GridCoords);
            var fuel = entityManager.SpawnEntity(FuelPrototype, map.GridCoords);
            var nonFuel = entityManager.SpawnEntity(NonFuelPrototype, map.GridCoords);
            var igniter = entityManager.SpawnEntity(IgniterPrototype, map.GridCoords);
            var component = entityManager.GetComponent<FuelableFireComponent>(fire);

            var igniteEmpty = new InteractUsingEvent(user, igniter, fire, map.GridCoords);
            entityManager.EventBus.RaiseLocalEvent(fire, igniteEmpty);
            Assert.Multiple(() =>
            {
                Assert.That(igniteEmpty.Handled, Is.True);
                Assert.That(component.Burning, Is.False);
                Assert.That(component.Fuel, Is.Zero);
            });

            var rejectGenericFlammable = new InteractUsingEvent(user, nonFuel, fire, map.GridCoords);
            entityManager.EventBus.RaiseLocalEvent(fire, rejectGenericFlammable);
            Assert.Multiple(() =>
            {
                Assert.That(rejectGenericFlammable.Handled, Is.False);
                Assert.That(component.Fuel, Is.Zero);
                Assert.That(entityManager.EntityExists(nonFuel), Is.True);
            });

            var refuel = new InteractUsingEvent(user, fuel, fire, map.GridCoords);
            entityManager.EventBus.RaiseLocalEvent(fire, refuel);
            Assert.Multiple(() =>
            {
                Assert.That(refuel.Handled, Is.True);
                Assert.That(component.Fuel, Is.EqualTo(37f));
                Assert.That(component.Burning, Is.False);
            });

            var ignite = new InteractUsingEvent(user, igniter, fire, map.GridCoords);
            entityManager.EventBus.RaiseLocalEvent(fire, ignite);
            Assert.Multiple(() =>
            {
                Assert.That(ignite.Handled, Is.True);
                Assert.That(component.Burning, Is.True);
            });

            var extinguish = new ActivateInWorldEvent(user, fire, true);
            entityManager.EventBus.RaiseLocalEvent(fire, extinguish);
            Assert.Multiple(() =>
            {
                Assert.That(extinguish.Handled, Is.True);
                Assert.That(component.Burning, Is.False);
                Assert.That(component.Fuel, Is.GreaterThan(0f));
            });
        });
    }

    [Test]
    public async Task ObviousFuelPrototypesDefineTheirOwnFuelAmount()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var map = await Pair.CreateTestMap();
        var expectedFuel = new Dictionary<string, float>
        {
            ["Paper"] = 5f,
            ["MaterialCloth1"] = 15f,
            ["MaterialCardboard1"] = 20f,
            ["BookBase"] = 30f,
            ["MaterialWoodPlank1"] = 60f,
            ["Coal1"] = 180f,
            ["Charcoal1"] = 240f,
            ["Log"] = 300f,
        };

        await server.WaitAssertion(() =>
        {
            foreach (var (prototype, expected) in expectedFuel)
            {
                var fuel = entityManager.SpawnEntity(prototype, map.GridCoords);
                var component = entityManager.GetComponent<FireFuelComponent>(fuel);
                Assert.That(component.Amount, Is.EqualTo(expected), prototype);
            }
        });
    }

    private static void AssertWindowState(IEntityManager entityManager, EntityUid window, bool open)
    {
        var openable = entityManager.GetComponent<OpenableWindowComponent>(window);
        var fixtures = entityManager.GetComponent<FixturesComponent>(window);
        var fixture = fixtures.Fixtures[openable.FixtureId];
        var occluder = entityManager.GetComponent<OccluderComponent>(window);
        var airtight = entityManager.GetComponent<Content.Server.Atmos.Components.AirtightComponent>(window);

        Assert.Multiple(() =>
        {
            Assert.That(openable.Open, Is.EqualTo(open));
            Assert.That(occluder.Enabled, Is.EqualTo(!open));
            Assert.That(airtight.AirBlocked, Is.EqualTo(!open));
            Assert.That(entityManager.HasComponent<ClimbableComponent>(window), Is.EqualTo(open));
            Assert.That(entityManager.HasComponent<BlockWeatherComponent>(window), Is.EqualTo(!open));
            Assert.That(fixture.CollisionMask, Is.EqualTo((int) (open
                ? CollisionGroup.TableMask
                : CollisionGroup.FullTileMask)));
            Assert.That(fixture.CollisionLayer, Is.EqualTo((int) (open
                ? CollisionGroup.TableLayer
                : CollisionGroup.FullTileLayer)));
        });
    }
}
