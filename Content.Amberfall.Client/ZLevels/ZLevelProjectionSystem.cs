using System.Numerics;
using Content.Amberfall.Shared.ZLevels;
using Content.Client.Graphics;
using Content.Client.Viewport;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Amberfall.Client.ZLevels;

/// <summary>Prepares cropped floor views deepest first, without re-entering the viewport renderer.</summary>
public sealed partial class ZLevelProjectionSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEyeManager _eyes = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ZLevelVisibilitySystem _visibility = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;

    private static readonly ProtoId<ShaderPrototype> ProjectionShader = "ZLevelProjection";
    private readonly OverlayResourceCache<ProjectionStack> _resources = new();
    private readonly List<Entity<SpriteComponent>> _hiddenSprites = new();
    private readonly List<ZLevelView> _views = new();
    private readonly Dictionary<IClydeViewport, ProjectionResources> _children = new();
    private ZLevelProjectionOverlay _overlay = default!;
    private IClydeViewport? _main;
    private EntityUid? _planMap;
    private Box2 _planBounds;
    private float _planAge = float.PositiveInfinity;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new ZLevelProjectionOverlay(this);
        _overlays.AddOverlay(_overlay);
        SubscribeLocalEvent<BeforeViewportRenderEvent>(OnBeforeRender);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _planAge += frameTime;
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        _overlay.Dispose();
        _resources.Dispose();
        _children.Clear();
        _views.Clear();
        _main = null;
        base.Shutdown();
    }

    private void OnBeforeRender(ref BeforeViewportRenderEvent args)
    {
        if (args.Control != _eyes.MainViewport)
            return;

        _children.Clear();
        _main = args.Viewport;
        var main = args.Viewport;
        if (main.Eye is not { } eye ||
            !_map.TryGetMap(eye.Position.MapId, out var upperMap) || upperMap is not { } upper ||
            !HasComp<ZLevelProjectionComponent>(upper))
        {
            _resources.Dispose();
            _views.Clear();
            _planMap = null;
            return;
        }

        var bounds = GetBounds(main).CalcBoundingBox();
        // Cache polygon subtraction at 10 Hz. Rebuild immediately when the camera
        // leaves the cached region; masks/camera transforms themselves update every frame.
        if (_planAge >= 0.1f || _planMap != upper ||
            !_planBounds.Contains(bounds.BottomLeft) || !_planBounds.Contains(bounds.TopRight))
        {
            _planBounds = bounds.Enlarged(1f);
            _visibility.BuildViews(upper, _planBounds, _views);
            _planMap = upper;
            _planAge = 0f;
        }

        var stack = _resources.GetForViewport(main, _ => new ProjectionStack());
        var count = 0;
        foreach (var view in _views)
        {
            if (!TryComp(view.SourceMap, out MapComponent? source) || source.MapId != view.MapId ||
                !TryGetCrop(main, view, out var crop))
                break;
            var size = GetRenderSize(main, crop, count);
            if (count == stack.Layers.Count)
                stack.Layers.Add(CreateResources(main, size));
            var layer = stack.Layers[count];
            var allocated = layer.Viewport.Size;
            // Grow as required, but only shrink once substantially smaller. This
            // avoids reallocating when a moving aperture straddles a bucket boundary.
            if (size.X > allocated.X || size.Y > allocated.Y ||
                size.X * 2 < allocated.X || size.Y * 2 < allocated.Y)
            {
                // Apply hysteresis independently on each axis: growing one side
                // must not shrink the other side back across its bucket boundary.
                size = new Vector2i(size.X > allocated.X || size.X * 2 < allocated.X ? size.X : allocated.X,
                    size.Y > allocated.Y || size.Y * 2 < allocated.Y ? size.Y : allocated.Y);
                layer.Viewport.Dispose();
                layer.Viewport = CreateViewport(size, layer.Eye);
            }
            layer.View = view;
            layer.Eye.Position = new MapCoordinates(main.LocalToWorld(crop.Center).Position, view.MapId);
            layer.Eye.Offset = Vector2.Zero;
            layer.Eye.Rotation = eye.Rotation;
            layer.Eye.Zoom = eye.Zoom;
            layer.Eye.DrawLight = eye.DrawLight;
            layer.Viewport.RenderScale = main.RenderScale * ((Vector2) layer.Viewport.Size / crop.Size);
            count++;
        }
        // Release targets for depths that have become occluded or unlinked.
        while (stack.Layers.Count > count)
        {
            stack.Layers[^1].Dispose();
            stack.Layers.RemoveAt(stack.Layers.Count - 1);
        }

        for (var depth = count - 1; depth >= 0; depth--)
        {
            var layer = stack.Layers[depth];
            RenderProjection(layer, main, args.RenderHandle);
            var parent = depth == 0 ? main : stack.Layers[depth - 1].Viewport;
            _children[parent] = layer;
        }
    }

    private static bool TryGetCrop(IClydeViewport main, ZLevelView view, out UIBox2 crop)
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        // Match WorldToLocal's arithmetic, but build the eye matrix once per crop
        // instead of once per aperture vertex (including its rotation/zoom work).
        main.Eye!.GetViewMatrix(out var viewMatrix, main.RenderScale);
        var pixelScale = new Vector2(1, -1) * EyeManager.PixelsPerMeter;
        var pixelOffset = main.Size / 2f;
        foreach (var polygon in view.Apertures)
        foreach (var point in polygon)
        {
            var pixel = Vector2.Transform(point, viewMatrix) * pixelScale + pixelOffset;
            min = Vector2.Min(min, pixel);
            max = Vector2.Max(max, pixel);
        }
        // Support margin for lighting and blur at the crop boundary.
        var padding = new Vector2(EyeManager.PixelsPerMeter) * main.RenderScale;
        min = Vector2.Max(Vector2.Zero, min - padding);
        max = Vector2.Min(main.Size, max + padding);
        crop = new UIBox2(min, max);
        return max.X > min.X && max.Y > min.Y;
    }

    private static Vector2i GetRenderSize(IClydeViewport main, UIBox2 crop, int depth)
    {
        // Distant floors need fewer pixels, not fewer animation frames.
        var native = crop.Size / main.RenderScale / (1f + depth * 0.5f);
        native *= MathF.Min(1f, 1024f / MathF.Max(native.X, native.Y));
        // Bucket allocation sizes to avoid recreating GPU targets on every camera movement.
        return new Vector2i(Math.Clamp((int) MathF.Ceiling(native.X / 64f) * 64, 64, 1024),
            Math.Clamp((int) MathF.Ceiling(native.Y / 64f) * 64, 64, 1024));
    }

    private IClydeViewport CreateViewport(Vector2i size, Eye eye)
    {
        var viewport = _clyde.CreateViewport(size, new TextureSampleParameters { Filter = true }, "z-level-below");
        viewport.Eye = eye;
        viewport.AutomaticRender = false;
        viewport.ClearColor = Color.Black;
        return viewport;
    }

    private ProjectionResources CreateResources(IClydeViewport main, Vector2i size)
    {
        var eye = new Eye { DrawFov = false };
        return new ProjectionResources(CreateViewport(size, eye), eye,
            _clyde.CreateRenderTarget(main.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb, false),
                new TextureSampleParameters { Filter = false }, "z-level-openings"),
            _prototypes.Index(ProjectionShader).InstanceUnique());
    }

    private void RenderLowerFloor(ProjectionResources resources, EntityUid lowerMap, bool renderEntities)
    {
        // Normally no sprite state needs changing. The legacy tile-only option also
        // has to exclude entities already present through another PVS subscription.
        // Restore immediately, including on renderer exceptions, before rendering the main view.
        try
        {
            if (!renderEntities)
            {
                var query = EntityQueryEnumerator<SpriteComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var sprite, out var transform))
                {
                    if (transform.MapUid != lowerMap || !sprite.Visible)
                        continue;

                    _hiddenSprites.Add((uid, sprite));
                    _sprites.SetVisible((uid, sprite), false);
                }
            }

            if (_hiddenSprites.Count > 0)
                _spriteTree.UpdateTreePositions();
            resources.Viewport.Render();
        }
        finally
        {
            foreach (var sprite in _hiddenSprites)
                _sprites.SetVisible((sprite.Owner, sprite.Comp), true);
            if (_hiddenSprites.Count > 0)
                _spriteTree.UpdateTreePositions();
            _hiddenSprites.Clear();
        }
    }

    private void RenderProjection(ProjectionResources resources, IClydeViewport main, IRenderHandle handle)
    {
        var world = handle.DrawingHandleWorld;
        var oldTransform = world.GetTransform();
        var oldShader = world.GetShader();
        var oldModulate = world.Modulate;
        var maskTransform = main.GetWorldToLocalMatrix();
        var redrawMask = !ReferenceEquals(resources.MaskView, resources.View) ||
            resources.MaskTransform != maskTransform;
        try
        {
            handle.RenderInRenderTarget(resources.Mask, () =>
            {
                handle.SetScissor(null);
                RenderLowerFloor(resources, resources.View.SourceMap, resources.View.RenderEntities);
                // The scene stays live every frame; only unchanged mask geometry is reused.
                if (!redrawMask)
                    return;
                world.UseShader(null);
                world.Modulate = Color.White;
                world.SetTransform(Matrix3x2.Identity);
                world.DrawRect(Box2.FromDimensions(Vector2.Zero, resources.Mask.Size), Color.Black);
                world.SetTransform(maskTransform);
                foreach (var polygon in resources.View.Apertures)
                    world.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, polygon.AsSpan(), Color.White);
            }, null);
            resources.MaskView = resources.View;
            resources.MaskTransform = maskTransform;
        }
        finally
        {
            world.SetTransform(oldTransform);
            world.UseShader(oldShader);
            world.Modulate = oldModulate;
        }
    }

    internal void Draw(in OverlayDrawArgs args)
    {
        if (_main == null || !_children.TryGetValue(args.Viewport, out var child) || child.Disposed)
            return;

        // All cameras have the main eye's rotation/zoom but different centres,
        // resolutions and extents. Map framebuffer UVs explicitly between them.
        var source = GetUvRect(args.Viewport, child.Viewport);
        var mask = GetUvRect(_main, args.Viewport);
        var shader = child.Shader;
        shader.SetParameter("lowerTexture", child.Viewport.RenderTarget.Texture);
        shader.SetParameter("openingMask", child.Mask.Texture);
        shader.SetParameter("sourceOrigin", source.Origin);
        shader.SetParameter("sourceSize", source.Size);
        shader.SetParameter("maskOrigin", mask.Origin);
        shader.SetParameter("maskSize", mask.Size);
        var sourceInMain = GetUvRect(_main, child.Viewport);
        shader.SetParameter("blurStep", new Vector2(child.View.BlurRadius) / ((Vector2) _main.Size * sourceInMain.Size));
        var world = args.WorldHandle;
        world.UseShader(shader);
        world.DrawRect(args.WorldBounds, Color.White);
        world.UseShader(null);
    }

    private static (Vector2 Origin, Vector2 Size) GetUvRect(IClydeViewport target, IClydeViewport source)
    {
        var topLeft = target.WorldToLocal(source.LocalToWorld(Vector2.Zero).Position) / target.Size;
        var bottomRight = target.WorldToLocal(source.LocalToWorld(source.Size).Position) / target.Size;
        return (new Vector2(topLeft.X, 1f - bottomRight.Y), bottomRight - topLeft);
    }

    private static Box2Rotated GetBounds(IClydeViewport viewport)
    {
        var eye = viewport.Eye!;
        var box = Box2.CenteredAround(eye.Position.Position + eye.Offset,
            viewport.Size / viewport.RenderScale / EyeManager.PixelsPerMeter * eye.Zoom);
        return new Box2Rotated(box, -eye.Rotation, box.Center);
    }

    private sealed class ProjectionStack : IDisposable
    {
        public readonly List<ProjectionResources> Layers = new();
        public void Dispose()
        {
            foreach (var layer in Layers)
                layer.Dispose();
            Layers.Clear();
        }
    }

    private sealed class ProjectionResources(IClydeViewport viewport, Eye eye, IRenderTexture mask, ShaderInstance shader) : IDisposable
    {
        public IClydeViewport Viewport = viewport;
        public readonly Eye Eye = eye;
        public readonly IRenderTexture Mask = mask;
        public readonly ShaderInstance Shader = shader;
        public ZLevelView View = default!;
        public ZLevelView? MaskView;
        public Matrix3x2 MaskTransform;
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
            Viewport.Dispose();
            Mask.Dispose();
            Shader.Dispose();
        }
    }
}
