using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Amberfall.Client.ZLevels;

public sealed class ZLevelProjectionOverlay : Overlay
{
    private readonly ZLevelProjectionSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public ZLevelProjectionOverlay(ZLevelProjectionSystem system)
    {
        _system = system;
        // Above parallax, below upper tiles, sprites and the upper floor's FOV.
        ZIndex = 1;
    }

    protected override void Draw(in OverlayDrawArgs args) => _system.Draw(in args);
}
