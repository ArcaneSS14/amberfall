namespace Content.Apotheosis;

/// <summary>
/// Stores solid fuel and exposes a fire only while that fuel is burning.
/// Fuel values are defined by FireFuelComponent on the consumed entities.
/// </summary>
[RegisterComponent]
public sealed partial class FuelableFireComponent : Component
{
    [DataField]
    public float Fuel;

    [DataField]
    public float Capacity = 300f;

    [DataField]
    public float BurnRate = 1f;

    [DataField]
    public bool Burning;
}
