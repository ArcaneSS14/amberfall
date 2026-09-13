namespace Content.Amberfall.Shared.ZLevels;

/// <summary>Convex aperture fragments in map coordinates, shared by rendering and PVS.</summary>
public static class ZLevelApertures
{
    public const int MaxDepth = 5;
    public const int MaxFragments = 1024;
    public const int MaxClipOperations = 131072;
    private const float Epsilon = 0.00001f;

    public static Vector2[] Rectangle(Box2 bounds) =>
    [bounds.BottomLeft, bounds.BottomRight, bounds.TopRight, bounds.TopLeft];

    /// <summary>Subtract a convex, counter-clockwise blocker. Returns false on budget exhaustion.</summary>
    public static bool Subtract(List<Vector2[]> regions, Vector2[] blocker, ref int operations)
    {
        var result = new List<Vector2[]>();
        var blockerBounds = Bounds(blocker);
        foreach (var region in regions)
        {
            if (!Bounds(region).Intersects(blockerBounds))
            {
                result.Add(region);
                continue;
            }

            var remaining = region;
            for (var edge = 0; edge < blocker.Length && remaining.Length >= 3; edge++)
            {
                if (++operations > MaxClipOperations)
                    return false;
                var a = blocker[edge];
                var b = blocker[(edge + 1) % blocker.Length];
                var outside = Clip(remaining, a, b, false);
                if (Area(outside) > Epsilon)
                    result.Add(outside);
                remaining = Clip(remaining, a, b, true);
            }
            if (result.Count > MaxFragments)
                return false;
        }

        if (result.Count > MaxFragments)
            return false;
        regions.Clear();
        regions.AddRange(result);
        return true;
    }

    private static Vector2[] Clip(Vector2[] polygon, Vector2 a, Vector2 b, bool inside)
    {
        if (polygon.Length == 0)
            return [];
        var direction = b - a;
        var sign = inside ? 1f : -1f;
        var accepted = 0;
        var intersections = 0;
        var previousAccepted = Cross(direction, polygon[^1] - a) * sign >= 0f;
        foreach (var point in polygon)
        {
            var currentAccepted = Cross(direction, point - a) * sign >= 0f;
            if (currentAccepted)
                accepted++;
            if (currentAccepted != previousAccepted)
                intersections++;
            previousAccepted = currentAccepted;
        }
        // Polygons are immutable: whole half-plane results can share the input.
        if (accepted == polygon.Length)
            return polygon;
        if (accepted == 0)
            return [];

        // Allocate only the final polygon. stackalloc emits unverifiable IL and
        // cannot be loaded by the stock client's content sandbox.
        var output = new Vector2[accepted + intersections];
        var count = 0;
        var previous = polygon[^1];
        var previousDistance = Cross(direction, previous - a) * sign;
        foreach (var current in polygon)
        {
            var distance = Cross(direction, current - a) * sign;
            if ((distance >= 0f) != (previousDistance >= 0f))
                output[count++] = Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance));
            if (distance >= 0f)
                output[count++] = current;
            previous = current;
            previousDistance = distance;
        }
        return output;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    public static float Area(Vector2[] polygon)
    {
        var area = 0f;
        // Work relative to a vertex so large map coordinates don't cancel small areas.
        for (var i = 1; i + 1 < polygon.Length; i++)
            area += Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
        return MathF.Abs(area) * 0.5f;
    }

    public static Box2 Bounds(Vector2[] polygon)
    {
        var min = polygon[0];
        var max = min;
        foreach (var point in polygon)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return new Box2(min, max);
    }

    public static Box2 Bounds(List<Vector2[]> regions)
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var region in regions)
        foreach (var point in region)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return new Box2(min, max);
    }

    /// <summary>Precompute bounds for repeated queries against immutable aperture geometry.</summary>
    public static Box2[] CacheBounds(List<Vector2[]> regions)
    {
        var bounds = new Box2[regions.Count];
        for (var i = 0; i < regions.Count; i++)
            bounds[i] = Bounds(regions[i]);
        return bounds;
    }

    /// <summary>Includes a small support margin for sprite extents and nearby light sources.</summary>
    public static bool IsNear(List<Vector2[]> regions, Vector2 point, float margin,
        IReadOnlyList<Box2>? cachedBounds = null)
    {
        var marginSquared = margin * margin;
        for (var index = 0; index < regions.Count; index++)
        {
            var polygon = regions[index];
            if (!(cachedBounds?[index] ?? Bounds(polygon)).Enlarged(margin).Contains(point))
                continue;
            var inside = true;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                if (Cross(b - a, point - a) < 0f)
                    inside = false;
                var segment = b - a;
                var length = segment.LengthSquared();
                var t = length > 0 ? Math.Clamp(Vector2.Dot(point - a, segment) / length, 0f, 1f) : 0f;
                if (Vector2.DistanceSquared(point, a + segment * t) <= marginSquared)
                    return true;
            }
            if (inside)
                return true;
        }
        return false;
    }
}

public sealed record ZLevelView(
    EntityUid SourceMap,
    MapId MapId,
    List<Vector2[]> Apertures,
    Box2 Bounds,
    float BlurRadius,
    bool RenderEntities)
{
    // Aperture geometry remains immutable for the lifetime of a visibility plan.
    public Box2[] ApertureBounds { get; } = ZLevelApertures.CacheBounds(Apertures);
}
