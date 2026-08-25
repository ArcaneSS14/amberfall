using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Server.Movement.Systems;

public sealed partial class JetpackSystem : SharedJetpackSystem
{
    protected override bool CanEnable(EntityUid uid, JetpackComponent component)
    {
        return base.CanEnable(uid, component);
    }
}
