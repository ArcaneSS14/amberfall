using Content.Amberfall.Shared.ZLevels;

namespace Content.Amberfall.Server.ZLevels;

/// <summary>
/// Connects adjacent maps and selectively sends visible lower-level data to nearby clients.
/// </summary>
public sealed partial class ZLevelSystem : EntitySystem
{
    private readonly HashSet<string> _dirtyGroups = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelComponent, MapInitEvent>(OnLevelMapInit);
        SubscribeLocalEvent<ZLevelComponent, ComponentShutdown>(OnLevelShutdown);
        InitializePvs();
    }

    public override void Shutdown()
    {
        ShutdownPvs();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        RebuildDirtyGroups();
        UpdatePvs(frameTime);
    }

    private void OnLevelMapInit(Entity<ZLevelComponent> entity, ref MapInitEvent args)
    {
        _dirtyGroups.Add(entity.Comp.Group);
    }

    private void OnLevelShutdown(Entity<ZLevelComponent> entity, ref ComponentShutdown args)
    {
        _dirtyGroups.Add(entity.Comp.Group);
    }
}
