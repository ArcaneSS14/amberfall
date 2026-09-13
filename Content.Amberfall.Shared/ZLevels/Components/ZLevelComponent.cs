namespace Content.Amberfall.Shared.ZLevels;

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
    /// Whether this level should visually project the floor below it.
    /// Maps remain physically separate; this only controls rendering.
    /// </summary>
    [DataField]
    public bool ProjectBelow = true;

    /// <summary>
    /// Blur sample offset applied to the floor immediately below this one, in screen pixels.
    /// </summary>
    [DataField]
    public float BlurRadius = 1.5f;

    /// <summary>
    /// Whether nearby objects from the map below should be sent and rendered.
    /// </summary>
    [DataField]
    public bool RenderEntities = true;
}
