using System.Numerics;
using Content.Amberfall.Common.ZLevels;

namespace Content.Amberfall.Shared.ZLevels;

/// <summary>
/// Moves physical entities to an adjacent linked map when they cross this entity.
/// </summary>
[RegisterComponent]
public sealed partial class ZTransitionComponent : Component
{
    [DataField]
    public ZLevelDirection Direction = ZLevelDirection.Down;

    [DataField]
    public Vector2 DestinationOffset;

    [DataField]
    public float Cooldown = 0.75f;
}
