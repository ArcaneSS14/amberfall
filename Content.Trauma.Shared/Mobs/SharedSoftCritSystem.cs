// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.Pulling.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Content.Trauma.Common.Body;
using Content.Trauma.Common.CCVar;
using Robust.Shared.Configuration;

namespace Content.Trauma.Shared.Mobs;

/// <summary>
/// Handles shared interactions with softcrit mobs.
/// </summary>
public abstract partial class SharedSoftCritSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    /// <summary>
    /// Speed modifier for softcrit mobs, on top of being forced to crawl.
    /// </summary>
    public float SoftCritSpeed = 0.5f;

    /// <summary>
    /// Inhaled gas modifier for softcrit mobs, makes it harder to breathe.
    /// This means you can't just crawl around forever if you aren't bleeding out.
    /// </summary>
    public float InhaleVolumeModifier = 0.3f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SoftCritMobComponent, ComponentStartup>(RefreshSpeed);
        SubscribeLocalEvent<SoftCritMobComponent, ComponentShutdown>(RefreshSpeed);
        SubscribeLocalEvent<MobStateComponent, AttemptStopPullingEvent>(OnAttemptStopPulling);
        SubscribeLocalEvent<SoftCritMobComponent, SpeechTypeOverrideEvent>(OnSpeechTypeOverride);
        SubscribeLocalEvent<SoftCritMobComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<SoftCritMobComponent, StandUpAttemptEvent>(OnStandUpAttempt);
        SubscribeLocalEvent<SoftCritMobComponent, UnbuckleAttemptEvent>(OnUnbuckleAttempt);

        Subs.CVar(_cfg, TraumaCVars.SoftCritMoveSpeed, x => SoftCritSpeed = x, true);
    }

    private void RefreshSpeed(EntityUid uid, SoftCritMobComponent ent, EntityEventArgs args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnAttemptStopPulling(Entity<MobStateComponent> ent, ref AttemptStopPullingEvent args)
    {
        // too weak to resist being pulled away into maints if you aren't alive
        if (ent.Comp.CurrentState != MobState.Alive && ent.Owner == args.User)
            args.Cancelled = true;
    }

    private void OnSpeechTypeOverride(Entity<SoftCritMobComponent> ent, ref SpeechTypeOverrideEvent args)
    {
        // too fucked up to speak properly
        if (args.DesiredType == InGameICChatType.Speak)
            args.DesiredType = InGameICChatType.Whisper;
    }

    private void OnRefreshSpeed(Entity<SoftCritMobComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(SoftCritSpeed);
    }

    private void OnStandUpAttempt(Entity<SoftCritMobComponent> ent, ref StandUpAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnUnbuckleAttempt(Entity<SoftCritMobComponent> ent, ref UnbuckleAttemptEvent args)
    {
        // can't unbuckle yourself if you are in softcrit
        args.Cancelled |= args.Buckle.Owner == args.User;
    }
}
