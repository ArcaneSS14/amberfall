using System.Numerics;
using Content.Apotheosis.Common.ZLevels;

namespace Content.Apotheosis.Shared.ZLevels;

/// <summary>
/// Moves physical entities to an adjacent linked map when they cross this entity.
/// </summary>
[RegisterComponent]
public sealed partial class ZTransitionComponent : Component
{
    [DataField]
    public ZLevelDirection Direction = ZLevelDirection.Down;

    /// <summary>
    /// Destination offset in map-space coordinates.
    /// </summary>
    [DataField]
    public Vector2 DestinationOffset;

    [DataField]
    public float Cooldown = 0.75f;
}
