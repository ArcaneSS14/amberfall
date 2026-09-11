using Robust.Client.Graphics;
using Robust.Shared.Analyzers;

namespace Content.Client.Viewport;

/// <summary>
/// Lets content prepare offscreen views before the primary viewport starts rendering.
/// Rendering another viewport from inside a world overlay would re-enter Clyde's renderer.
/// </summary>
[ByRefEvent]
public readonly record struct BeforeViewportRenderEvent(
    ScalingViewport Control,
    IClydeViewport Viewport,
    IRenderHandle RenderHandle);
