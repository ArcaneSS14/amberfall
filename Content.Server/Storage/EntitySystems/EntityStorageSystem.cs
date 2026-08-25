using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server.Storage.EntitySystems;

public sealed partial class EntityStorageSystem : SharedEntityStorageSystem
{
    [Dependency] private ConstructionSystem _construction = default!;

    public override void Initialize()
    {
        base.Initialize();

    }

    protected override void OnComponentInit(EntityUid uid, EntityStorageComponent component, ComponentInit args)
    {
        base.OnComponentInit(uid, component, args);

        if (TryComp<ConstructionComponent>(uid, out var construction))
            _construction.AddContainer(uid, ContainerName, construction);
    }

    protected override void TakeGas(EntityUid uid, EntityStorageComponent component)
    {
    }

    public override void ReleaseGas(EntityUid uid, EntityStorageComponent component)
    {
    }
}
