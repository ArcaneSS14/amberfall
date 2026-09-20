using System.Numerics;
using Content.Amberfall.Shared.ZLevels;
using Content.Medical.Common.Targeting;
using Content.Medical.Shared.Body;
using Content.Medical.Shared.Traumas;
using Content.Medical.Shared.Wounds;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Amberfall.Server.ZLevels;

public sealed partial class ZTransitionSystem
{
    private static readonly DamageSpecifier FallDamage = new()
    {
        DamageDict = { ["Blunt"] = 40 },
    };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private TraumaSystem _trauma = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private readonly HashSet<EntityUid> _transitioning = new();

    private bool CanTransition(EntityUid uid)
    {
        if (HasComp<MapGridComponent>(uid) || _transitioning.Contains(uid))
            return false;

        if (!TryComp(uid, out ZTransitionCooldownComponent? cooldown))
            return true;

        if (cooldown.Until > _timing.CurTime)
            return false;

        RemCompDeferred(uid, cooldown);
        return true;
    }

    private void Transition(
        EntityUid uid,
        EntityUid destinationMap,
        EntityUid destinationParent,
        Vector2 mapPosition,
        TimeSpan cooldown,
        bool applyFallDamage)
    {
        if (!Exists(destinationMap) ||
            !Exists(destinationParent) ||
            !TryComp(destinationMap, out MapComponent? mapComponent))
        {
            return;
        }

        var cooldownComponent = EnsureComp<ZTransitionCooldownComponent>(uid);
        cooldownComponent.Until = _timing.CurTime + cooldown;

        var sourceMap = Transform(uid).MapUid;
        var worldRotation = _transform.GetWorldRotation(uid);
        var mapCoordinates = new MapCoordinates(mapPosition, mapComponent.MapId);
        var destinationCoordinates = _transform.ToCoordinates(destinationParent, mapCoordinates);

        _transitioning.Add(uid);
        try
        {
            _transform.SetCoordinates(uid, destinationCoordinates);
            _transform.SetWorldRotation(uid, worldRotation);

            if (sourceMap is { } source)
            {
                var transitionEvent = new ZLevelTransitionedEvent(
                    uid,
                    source,
                    destinationMap,
                    mapPosition);
                RaiseLocalEvent(transitionEvent);
            }

            if (applyFallDamage)
            {
                _damageable.ApplyDamageToBodyParts(
                    uid,
                    FallDamage,
                    null,
                    false,
                    true,
                    TargetBodyPart.FullLegs,
                    1.5f);

                BreakLegBones(uid);

                _stun.TryKnockdown(uid, TimeSpan.FromSeconds(4));
            }
        }
        finally
        {
            _transitioning.Remove(uid);
        }
    }

    private void BreakLegBones(EntityUid uid)
    {
        foreach (var part in _body.GetOrgans<WoundableComponent>(uid))
        {
            if (HasComp<LegComponent>(part) && _trauma.GetBone(part.AsNullable()) is { } bone)
                _trauma.SetBoneIntegrity(bone, 0, bone.Comp);
        }
    }
}
