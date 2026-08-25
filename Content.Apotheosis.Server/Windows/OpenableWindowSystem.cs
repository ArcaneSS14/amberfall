using Content.Server.Access;
using Content.Shared.Climbing.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Toggleable;
using Content.Shared.Weather;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Apotheosis;

public sealed partial class OpenableWindowSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private OccluderSystem _occluder = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OpenableWindowComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OpenableWindowComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnMapInit(Entity<OpenableWindowComponent> ent, ref MapInitEvent args)
    {
        SetOpen(ent, ent.Comp.Open, playSound: false);
    }

    private void OnActivate(Entity<OpenableWindowComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        SetOpen(ent, !ent.Comp.Open);
        args.Handled = true;
    }

    public void SetOpen(Entity<OpenableWindowComponent> ent, bool open, bool playSound = true)
    {
        var (uid, component) = ent;
        component.Open = open;

        if (TryComp<FixturesComponent>(uid, out var fixtures) &&
            fixtures.Fixtures.TryGetValue(component.FixtureId, out var fixture))
        {
            var mask = open ? CollisionGroup.TableMask : CollisionGroup.FullTileMask;
            var layer = open ? CollisionGroup.TableLayer : CollisionGroup.FullTileLayer;
            _physics.SetCollisionMask(uid, component.FixtureId, fixture, (int) mask, fixtures);
            _physics.SetCollisionLayer(uid, component.FixtureId, fixture, (int) layer, fixtures);
        }

        _occluder.SetEnabled(uid, !open);

        if (open)
        {
            var climbable = EnsureComp<ClimbableComponent>(uid);
            climbable.ClimbDelay = component.ClimbDelay;
            Dirty(uid, climbable);
            RemComp<BlockWeatherComponent>(uid);
        }
        else
        {
            RemComp<ClimbableComponent>(uid);
            EnsureComp<BlockWeatherComponent>(uid);
        }

        _appearance.SetData(uid, ToggleableVisuals.Enabled, open);
        RaiseLocalEvent(new AccessReaderChangeEvent(uid, !open));
        RaiseLocalEvent(uid, new DoorStateChangedEvent(open ? DoorState.Open : DoorState.Closed));

        if (playSound)
            _audio.PlayPvs(open ? component.OpenSound : component.CloseSound, uid);
    }
}
