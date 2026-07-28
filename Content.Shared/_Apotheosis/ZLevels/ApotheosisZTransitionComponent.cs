using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Apotheosis.ZLevels;

/// <summary>
/// Moves physical entities to the adjacent linked Z-level when they cross this entity.
/// </summary>
[RegisterComponent]
public sealed partial class ApotheosisZTransitionComponent : Component
{
    [DataField]
    public ApotheosisZDirection Direction = ApotheosisZDirection.Down;

    [DataField]
    public Vector2 DestinationOffset;

    [DataField]
    public float Cooldown = 0.75f;
}

public enum ApotheosisZDirection : byte
{
    Up,
    Down,
}
