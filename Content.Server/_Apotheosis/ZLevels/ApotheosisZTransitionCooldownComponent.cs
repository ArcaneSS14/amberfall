using Robust.Shared.GameObjects;

namespace Content.Server._Apotheosis.ZLevels;

[RegisterComponent]
[UnsavedComponent]
internal sealed partial class ApotheosisZTransitionCooldownComponent : Component
{
    public TimeSpan Until;
}
