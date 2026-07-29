namespace Content.Apotheosis.Shared.ZLevels;

[RegisterComponent]
[UnsavedComponent]
public sealed partial class ZTransitionCooldownComponent : Component
{
    public TimeSpan Until;
}
