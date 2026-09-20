namespace Content.Amberfall;

/// <summary>
/// Stores and burns solid fuel without relying on atmosphere simulation.
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

    [DataField]
    public bool InfiniteFuel;

    [DataField]
    public bool CanExtinguish = true;
}
