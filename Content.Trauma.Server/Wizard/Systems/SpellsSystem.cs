using Content.Trauma.Common.Wizard;
using Content.Trauma.Shared.Wizard;

namespace Content.Trauma.Server.Wizard.Systems;

public sealed partial class SpellsSystem : SharedSpellsSystem
{
    public override event Action? StopTargeting
    {
        add { }
        remove { }
    }

    public override void SetSwapSecondaryTarget(EntityUid user, EntityUid? target, EntityUid action)
    {
    }

    protected override void CreateChargeEffect(EntityUid uid, ChargeSpellRaysEffectEvent ev)
    {
    }
}
