// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Lavaland.Common.Mining;
using Content.Shared.Access.Systems;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Trauma.Common.Salvage;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Trauma.Shared.Salvage.Systems;

public sealed partial class MiningPointsSystem : CommonMiningPointsSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<MiningPointsComponent> _query;

    public override void Initialize()
    {
        base.Initialize();

        _query = GetEntityQuery<MiningPointsComponent>();

        SubscribeLocalEvent<MiningPointsLatheComponent, MaterialEntityInsertedEvent>(OnMaterialEntityInserted);
        Subs.BuiEvents<MiningPointsLatheComponent>(LatheUiKey.Key, subscriptions =>
        {
            subscriptions.Event<LatheClaimMiningPointsMessage>(OnClaimMiningPoints);
        });
    }

    private void OnMaterialEntityInserted(
        Entity<MiningPointsLatheComponent> entity,
        ref MaterialEntityInsertedEvent args)
    {
        if (!_timing.IsFirstTimePredicted ||
            !TryComp<UnclaimedOreComponent>(args.Inserted, out var ore))
        {
            return;
        }

        var points = ore.MiningPoints * args.Count;
        if (points > 0)
            AddPoints(entity.Owner, (uint) points);
    }

    private void OnClaimMiningPoints(
        Entity<MiningPointsLatheComponent> entity,
        ref LatheClaimMiningPointsMessage args)
    {
        var source = _query.Comp(entity);
        if (source.Points == 0 || GetPointHolder(args.Actor) is not { } destination)
            return;

        var points = source.Points;
        TransferAll((entity.Owner, source), destination);

        var claimed = new MiningPointsClaimedEvent(args.Actor, (int) points);
        RaiseLocalEvent(entity, ref claimed, true);
    }

    public override bool CanClaimPoints(EntityUid user)
    {
        return GetPointHolder(user) != null;
    }

    public override bool UserHasPoints(EntityUid user, uint points)
    {
        return GetPointHolder(user)?.Comp is { } component && component.Points >= points;
    }

    public override Entity<MiningPointsComponent?>? TryFindIdCard(EntityUid user)
    {
        if (!_idCard.TryFindIdCard(user, out var idCard) ||
            !_query.TryComp(idCard, out var points))
        {
            return null;
        }

        return (idCard, points);
    }

    public override bool RemovePoints(Entity<MiningPointsComponent?> entity, uint amount)
    {
        if (!_query.Resolve(entity, ref entity.Comp) || amount > entity.Comp.Points)
            return false;

        entity.Comp.Points -= amount;
        Dirty(entity);
        return true;
    }

    public bool AddPoints(Entity<MiningPointsComponent?> entity, uint amount)
    {
        if (!_query.Resolve(entity, ref entity.Comp))
            return false;

        entity.Comp.Points += amount;
        Dirty(entity);
        return true;
    }

    public bool Transfer(
        Entity<MiningPointsComponent?> source,
        Entity<MiningPointsComponent?> destination,
        uint amount)
    {
        if (amount == 0)
            return true;

        if (!_query.Resolve(source, ref source.Comp) ||
            !_query.Resolve(destination, ref destination.Comp) ||
            !RemovePoints(source, amount))
        {
            return false;
        }

        AddPoints(destination, amount);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), source.Owner);
        return true;
    }

    public bool TransferAll(
        Entity<MiningPointsComponent?> source,
        Entity<MiningPointsComponent?> destination)
    {
        return _query.Resolve(source, ref source.Comp) &&
               Transfer(source, destination, source.Comp.Points);
    }

    private Entity<MiningPointsComponent?>? GetPointHolder(EntityUid user)
    {
        if (TryComp<MiningPointsComponent>(user, out var points))
            return (user, points);

        return TryFindIdCard(user);
    }
}
