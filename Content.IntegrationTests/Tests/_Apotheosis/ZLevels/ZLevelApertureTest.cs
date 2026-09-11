using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Apotheosis.Shared.ZLevels;
using Content.Apotheosis.Server.ZLevels;
using Content.IntegrationTests.Fixtures;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Apotheosis.ZLevels;

[TestFixture]
public sealed class ZLevelApertureTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task OnlyAlignedAperturesReachDeeperMaps()
    {
        await Server.WaitAssertion(() =>
        {
            var map = SEntMan.System<MapSystem>();
            var links = SEntMan.System<ZLevelSystem>();
            var visibility = SEntMan.System<ZLevelVisibilitySystem>();
            var maps = new List<EntityUid>();
            var grids = new List<Entity<MapGridComponent>>();
            for (var depth = 0; depth < 5; depth++)
            {
                maps.Add(map.CreateMap(out var id));
                var grid = map.CreateGridEntity(id);
                grids.Add(grid);
                for (var x = -4; x <= 4; x++)
                for (var y = -4; y <= 4; y++)
                {
                    if (x != 0 || y != 0)
                        map.SetTile(grid, new Vector2i(x, y), new Tile(1));
                }
                if (depth > 0)
                    Assert.That(links.LinkMaps(maps[depth - 1], maps[depth]), Is.True);
            }
            var bounds = new Box2(-2, -2, 3, 3);
            var views = new List<ZLevelView>();
            visibility.BuildViews(maps[0], bounds, views);
            Assert.That(views, Has.Count.EqualTo(3), "Depth is bounded even in a longer open shaft.");
            Assert.That(views[2].SourceMap, Is.EqualTo(maps[3]));
            Assert.That(Vector2.Distance(views[2].Bounds.Size, Vector2.One), Is.LessThan(0.0001f));
            Assert.That(ZLevelApertures.IsNear(views[2].Apertures, new Vector2(0.5f), 0f), Is.True);
            Assert.That(ZLevelApertures.IsNear(views[2].Apertures, new Vector2(2.5f), 0f), Is.False);

            // Both floors contain holes, but at different coordinates: no sightline to -2.
            map.SetTile(grids[1], Vector2i.Zero, new Tile(1));
            map.SetTile(grids[1], new Vector2i(2, 0), Tile.Empty);
            visibility.BuildViews(maps[0], bounds, views);
            Assert.That(views, Has.Count.EqualTo(1));

            map.SetTile(grids[1], Vector2i.Zero, Tile.Empty);
            SEntMan.System<TransformSystem>().SetWorldRotation(grids[1].Owner, Angle.FromDegrees(45));
            visibility.BuildViews(maps[0], bounds, views);
            Assert.That(views, Has.Count.EqualTo(3), "A rotated overlapping hole remains visible.");
            Assert.That(ZLevelApertures.IsNear(views[1].Apertures, new Vector2(0.1f, 0.5f), 0f), Is.True);
            Assert.That(ZLevelApertures.IsNear(views[1].Apertures, new Vector2(0.9f, 0.1f), 0f), Is.False);

            links.UnlinkLower(maps[1]);
            visibility.BuildViews(maps[0], bounds, views);
            Assert.That(views, Has.Count.EqualTo(1));
            map.SetTile(grids[0], Vector2i.Zero, new Tile(1));
            visibility.BuildViews(maps[0], bounds, views);
            Assert.That(views, Is.Empty);
            foreach (var uid in maps)
                SEntMan.DeleteEntity(uid);
        });
    }
}

[TestFixture]
public sealed class ZLevelApertureGeometryTest
{
    [Test]
    public void ClippingAllocatesLessThanTheReference()
    {
        var rectangle = ZLevelApertures.Rectangle(new Box2(0, 0, 4, 4));
        var blocker = ZLevelApertures.Rectangle(new Box2(1, 1, 3, 3));
        void Optimized()
        {
            var regions = new List<Vector2[]> { rectangle };
            var operations = 0;
            ZLevelApertures.Subtract(regions, blocker, ref operations);
        }
        void Reference() => ReferenceSubtract(new List<Vector2[]> { rectangle }, blocker);
        for (var i = 0; i < 200; i++)
        {
            Optimized();
            Reference();
        }
        static long Allocated(Action action)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
                action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var reference = Allocated(Reference);
        var optimized = Allocated(Optimized);
        TestContext.Progress.WriteLine($"1000 rectangle subtractions: reference {reference} B; optimized {optimized} B.");
        Assert.That(optimized, Is.LessThan(reference));
    }

    [Test]
    public void OptimizedClippingMatchesReferenceForRotatedAndTouchingFloors()
    {
        var random = new System.Random(4187);
        for (var sample = 0; sample < 200; sample++)
        {
            var original = ZLevelApertures.Rectangle(new Box2(-4, -4, 4, 4));
            var actual = new List<Vector2[]> { original };
            var expected = new List<Vector2[]> { original };
            var operations = 0;
            for (var floor = 0; floor < 8; floor++)
            {
                var blocker = ZLevelApertures.Rectangle(new Box2(-1, -1, 1, 1));
                var rotation = sample % 2 == 0 ? 0f : (float) random.NextDouble() * MathF.Tau;
                var transform = Matrix3x2.CreateRotation(rotation) *
                    Matrix3x2.CreateTranslation(random.Next(-4, 5), random.Next(-4, 5));
                for (var i = 0; i < blocker.Length; i++)
                    blocker[i] = Vector2.Transform(blocker[i], transform);
                expected = ReferenceSubtract(expected, blocker);
                Assert.That(ZLevelApertures.Subtract(actual, blocker, ref operations), Is.True);
                Assert.That(actual.Count, Is.EqualTo(expected.Count));
                for (var i = 0; i < actual.Count; i++)
                    Assert.That(actual[i], Is.EqualTo(expected[i]), $"Sample {sample}, floor {floor}, fragment {i}");
            }
            Assert.That(original, Is.EqualTo(ZLevelApertures.Rectangle(new Box2(-4, -4, 4, 4))),
                "Sharing unchanged polygons must not mutate earlier views.");
            var cached = ZLevelApertures.CacheBounds(actual);
            for (var i = 0; i < 30; i++)
            {
                var point = new Vector2((float) random.NextDouble() * 12 - 6, (float) random.NextDouble() * 12 - 6);
                var margin = i % 3;
                Assert.That(ZLevelApertures.IsNear(actual, point, margin, cached),
                    Is.EqualTo(ZLevelApertures.IsNear(actual, point, margin)));
            }
        }
    }

    // Previous allocating implementation retained as a differential oracle.
    private static List<Vector2[]> ReferenceSubtract(List<Vector2[]> regions, Vector2[] blocker)
    {
        var result = new List<Vector2[]>();
        foreach (var region in regions)
        {
            if (!ZLevelApertures.Bounds(region).Intersects(ZLevelApertures.Bounds(blocker)))
            {
                result.Add(region);
                continue;
            }
            var remaining = region;
            for (var edge = 0; edge < blocker.Length && remaining.Length >= 3; edge++)
            {
                var a = blocker[edge];
                var b = blocker[(edge + 1) % blocker.Length];
                var outside = ReferenceClip(remaining, a, b, false);
                if (ZLevelApertures.Area(outside) > 0.00001f)
                    result.Add(outside);
                remaining = ReferenceClip(remaining, a, b, true);
            }
        }
        return result;
    }

    private static Vector2[] ReferenceClip(Vector2[] polygon, Vector2 a, Vector2 b, bool inside)
    {
        var output = new List<Vector2>(polygon.Length + 1);
        var previous = polygon[^1];
        float Distance(Vector2 point) => ((b.X - a.X) * (point.Y - a.Y) -
            (b.Y - a.Y) * (point.X - a.X)) * (inside ? 1f : -1f);
        var previousDistance = Distance(previous);
        foreach (var current in polygon)
        {
            var distance = Distance(current);
            if ((distance >= 0f) != (previousDistance >= 0f))
                output.Add(Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance)));
            if (distance >= 0f)
                output.Add(current);
            previous = current;
            previousDistance = distance;
        }
        return output.ToArray();
    }

    [Test]
    public void SubtractingAnInteriorFloorPreservesAreaAndRejectsItsCentre()
    {
        var regions = new List<Vector2[]> { ZLevelApertures.Rectangle(new Box2(0, 0, 4, 4)) };
        var operations = 0;
        Assert.That(ZLevelApertures.Subtract(regions,
            ZLevelApertures.Rectangle(new Box2(1, 1, 3, 3)), ref operations), Is.True);
        Assert.That(regions.Sum(ZLevelApertures.Area), Is.EqualTo(12f).Within(0.001f));
        Assert.That(ZLevelApertures.IsNear(regions, new Vector2(2), 0f), Is.False);
        Assert.That(ZLevelApertures.IsNear(regions, new Vector2(0.5f), 0f), Is.True);
        Assert.That(ZLevelApertures.IsNear(regions, new Vector2(1.1f, 2), 0.2f), Is.True);
    }

    [Test]
    public void BudgetExhaustionIsExplicitAndDoesNotMutateExistingApertures()
    {
        var regions = new List<Vector2[]> { ZLevelApertures.Rectangle(new Box2(0, 0, 4, 4)) };
        var operations = ZLevelApertures.MaxClipOperations;
        Assert.That(ZLevelApertures.Subtract(regions,
            ZLevelApertures.Rectangle(new Box2(1, 1, 3, 3)), ref operations), Is.False);
        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.That(ZLevelApertures.Area(regions[0]), Is.EqualTo(16f));
    }
}
