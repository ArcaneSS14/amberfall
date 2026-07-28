using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Apotheosis.ZLevels;

/// <summary>
/// Marks a grid as one visual floor in a fake Z-level stack.
/// Grids in the same group are aligned by their local tile coordinates.
/// </summary>
[RegisterComponent]
public sealed partial class ApotheosisZLevelComponent : Component
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
    /// Whether objects visible through the floor should be sent and rendered.
    /// </summary>
    [DataField]
    public bool RenderEntities = true;
}
