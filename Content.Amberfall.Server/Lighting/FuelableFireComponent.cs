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

    /// <summary>
    /// Keeps the fire burning without consuming or requiring stored fuel.
    /// </summary>
    [DataField]
    public bool InfiniteFuel;

    /// <summary>
    /// Whether players can extinguish the fire by activating it in-world.
    /// </summary>
    [DataField]
    public bool CanExtinguish = true;
}
