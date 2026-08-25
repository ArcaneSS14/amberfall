namespace Content.Apotheosis;

/// <summary>
/// Solid fuel measured in fuel units per entity or stack item.
/// </summary>
[RegisterComponent]
public sealed partial class FireFuelComponent : Component
{
    [DataField]
    public float Amount;
}
