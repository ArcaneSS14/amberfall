using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Content.Server.Explosion.EntitySystems;

[Flags]
public enum ExplosionDirection
{
    Invalid = 0,
    North = 1 << 0,
    South = 1 << 1,
    East = 1 << 2,
    West = 1 << 3,
    NorthEast = North | East,
    SouthEast = South | East,
    NorthWest = North | West,
    SouthWest = South | West,
    All = North | South | East | West,
}

public static class ExplosionDirectionHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExplosionDirection ToOppositeDir(this int index) =>
        (ExplosionDirection) (1 << (index ^ 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFlagSet(this ExplosionDirection direction, ExplosionDirection other) =>
        (direction & other) == other;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [PublicAPI]
    public static Vector2i Offset(this Vector2i position, ExplosionDirection direction)
    {
        var bits = (byte) direction;
        var dx = ((bits >> 2) & 1) - ((bits >> 3) & 1);
        var dy = ((bits >> 0) & 1) - ((bits >> 1) & 1);
        return position + new Vector2i(dx, dy);
    }
}
