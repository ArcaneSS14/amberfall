namespace Content.Apotheosis;

/// <summary>
/// Marks an entity as solid fuel and stores how many fuel units one entity, or
/// one unit of a stack, adds to a fuelable fire.
/// </summary>
[RegisterComponent]
public sealed partial class FireFuelComponent : Component
{
    [DataField]
    public float Amount;
}
