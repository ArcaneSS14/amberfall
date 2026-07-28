using Robust.Shared.GameObjects;

namespace Content.Server._Apotheosis.ZLevels;

/// <summary>
/// Runtime links to the grids immediately above and below this grid.
/// </summary>
[RegisterComponent]
[UnsavedComponent]
internal sealed partial class ApotheosisZLinkComponent : Component
{
    public EntityUid? UpperGrid;
    public EntityUid? LowerGrid;
}
