namespace Content.Apotheosis.Shared.ZLevels;

/// <summary>
/// Marks a map as one floor in a fake Z-level stack.
/// Maps in one group share the same map-space coordinate system.
/// </summary>
[RegisterComponent]
public sealed partial class ZLevelComponent : Component
{
    [DataField(required: true)]
    public string Group = string.Empty;

    [DataField]
    public int Level;

    /// <summary>
    /// Fraction of brightness removed from the floor immediately below this one.
    /// </summary>
    [DataField]
    public float Darkness = 0.45f;

    /// <summary>
    /// Whether nearby objects from the map below should be sent and rendered.
    /// </summary>
    [DataField]
    public bool RenderEntities = true;
}
