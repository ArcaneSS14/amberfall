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

    [DataField]
    public bool ProjectBelow = true;

    [DataField]
    public float BlurRadius = 1.5f;

    [DataField]
    public bool RenderEntities = true;
}
