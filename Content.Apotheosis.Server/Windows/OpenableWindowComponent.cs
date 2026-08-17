using Robust.Shared.Audio;

namespace Content.Apotheosis;

/// <summary>
/// A window that blocks movement, air and vision while closed and becomes
/// climbable while open.
/// </summary>
[RegisterComponent]
public sealed partial class OpenableWindowComponent : Component
{
    [DataField]
    public bool Open;

    [DataField]
    public string FixtureId = "fix1";

    [DataField]
    public float ClimbDelay = 1.5f;

    [DataField]
    public SoundSpecifier OpenSound = new SoundPathSpecifier("/Audio/Effects/door_open.ogg");

    [DataField]
    public SoundSpecifier CloseSound = new SoundPathSpecifier("/Audio/Effects/door_close.ogg");
}
