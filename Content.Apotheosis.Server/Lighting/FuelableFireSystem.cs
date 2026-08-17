using Content.Server.Stack;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.IgnitionSource;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Temperature;
using Content.Shared.Toggleable;
using Robust.Shared.Audio.Systems;

namespace Content.Apotheosis;

public sealed partial class FuelableFireSystem : EntitySystem
{
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedIgnitionSourceSystem _ignition = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FuelableFireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FuelableFireComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FuelableFireComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<FuelableFireComponent, IsHotEvent>(OnIsHot);
        SubscribeLocalEvent<FuelableFireComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<FuelableFireComponent>();
        while (query.MoveNext(out var uid, out var fire))
        {
            if (!fire.Burning)
                continue;

            fire.Fuel = MathF.Max(0f, fire.Fuel - fire.BurnRate * frameTime);
            if (fire.Fuel <= 0f)
                SetBurning((uid, fire), false);
        }
    }

    private void OnMapInit(Entity<FuelableFireComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Capacity = MathF.Max(0f, ent.Comp.Capacity);
        ent.Comp.BurnRate = MathF.Max(0f, ent.Comp.BurnRate);
        ent.Comp.Fuel = Math.Clamp(ent.Comp.Fuel, 0f, ent.Comp.Capacity);
        SetBurning(ent, ent.Comp.Burning && ent.Comp.Fuel > 0f);
    }

    private void OnInteractUsing(Entity<FuelableFireComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var hot = new IsHotEvent();
        RaiseLocalEvent(args.Used, hot);

        if (hot.IsHot)
        {
            if (ent.Comp.Fuel <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("fuelable-fire-no-fuel"), ent, args.User);
            }
            else if (!ent.Comp.Burning)
            {
                SetBurning(ent, true);
                _popup.PopupEntity(Loc.GetString("fuelable-fire-ignited"), ent, args.User);
            }

            args.Handled = true;
            return;
        }

        if (!TryComp<FireFuelComponent>(args.Used, out var fuel))
            return;

        var amount = MathF.Max(0f, fuel.Amount);
        if (amount <= 0f)
            return;

        var availableCapacity = ent.Comp.Capacity - ent.Comp.Fuel;
        if (amount > availableCapacity)
        {
            var message = availableCapacity <= 0f
                ? "fuelable-fire-full"
                : "fuelable-fire-not-enough-room";
            _popup.PopupEntity(Loc.GetString(message), ent, args.User);
            args.Handled = true;
            return;
        }

        ent.Comp.Fuel += amount;
        ConsumeFuelEntity(args.Used);
        _popup.PopupEntity(Loc.GetString("fuelable-fire-fueled", ("amount", MathF.Ceiling(amount))), ent, args.User);
        args.Handled = true;
    }

    private void OnActivate(Entity<FuelableFireComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !ent.Comp.Burning)
            return;

        SetBurning(ent, false);
        _popup.PopupEntity(Loc.GetString("fuelable-fire-extinguished"), ent, args.User);
        args.Handled = true;
    }

    private void OnIsHot(Entity<FuelableFireComponent> ent, ref IsHotEvent args)
    {
        args.IsHot |= ent.Comp.Burning;
    }

    private void OnExamined(Entity<FuelableFireComponent> ent, ref ExaminedEvent args)
    {
        var percent = ent.Comp.Capacity <= 0f ? 0 : (int) MathF.Round(ent.Comp.Fuel / ent.Comp.Capacity * 100f);
        args.PushMarkup(Loc.GetString("fuelable-fire-examine", ("percent", percent)));
    }

    private void ConsumeFuelEntity(EntityUid fuel)
    {
        if (TryComp<StackComponent>(fuel, out var stack))
        {
            _stack.ReduceCount((fuel, stack), 1);
            return;
        }

        QueueDel(fuel);
    }

    private void SetBurning(Entity<FuelableFireComponent> ent, bool burning)
    {
        if (burning && ent.Comp.Fuel <= 0f)
            burning = false;

        var changed = ent.Comp.Burning != burning;
        ent.Comp.Burning = burning;
        _light.SetEnabled(ent, burning);
        _ignition.SetIgnited(ent.Owner, burning);
        _ambient.SetAmbience(ent, burning);
        _appearance.SetData(ent, ToggleableVisuals.Enabled, burning);

        if (!changed)
            return;

        if (burning)
        {
            var ignited = new IgnitedEvent();
            RaiseLocalEvent(ent, ref ignited);
        }
        else
        {
            var extinguished = new ExtinguishedEvent();
            RaiseLocalEvent(ent, ref extinguished);
        }
    }
}
