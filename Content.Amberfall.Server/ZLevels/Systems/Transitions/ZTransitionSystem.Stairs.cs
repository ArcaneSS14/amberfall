using System.Numerics;
using Content.Amberfall.Common.ZLevels;
using Content.Amberfall.Shared.ZLevels;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Amberfall.Server.ZLevels;

public sealed partial class ZTransitionSystem
{
    private void OnStepTriggered(
        Entity<ZTransitionComponent> stairs,
        ref StepTriggeredOffEvent args)
    {
        if (!TryComp(args.Tripper, out PhysicsComponent? physics) ||
            physics.BodyType == BodyType.Static ||
            Transform(args.Tripper).Anchored ||
            !CanTransition(args.Tripper))
        {
            return;
        }

        var stairsTransform = Transform(stairs);
        if (stairsTransform.MapUid is not { } sourceMap ||
            !TryComp(sourceMap, out ZLevelLinkComponent? link))
        {
            return;
        }

        var destinationMap = stairs.Comp.Direction switch
        {
            ZLevelDirection.Up => link.UpperMap,
            ZLevelDirection.Down => link.LowerMap,
            _ => null,
        };

        if (destinationMap is not { } targetMap ||
            !TryComp(targetMap, out MapComponent? targetMapComponent))
        {
            return;
        }

        var tripperTransform = Transform(args.Tripper);
        if (tripperTransform.MapUid != sourceMap)
            return;

        var destinationPosition =
            _transform.GetMapCoordinates(tripperTransform).Position +
            stairs.Comp.DestinationOffset;
        var destinationCoordinates = new MapCoordinates(
            destinationPosition,
            targetMapComponent.MapId);

        var destinationParent = _map.TryFindGridAt(
            destinationCoordinates,
            out var targetGrid,
            out _)
            ? targetGrid
            : targetMap;

        Transition(
            args.Tripper,
            targetMap,
            destinationParent,
            destinationPosition,
            TimeSpan.FromSeconds(Math.Max(0f, stairs.Comp.Cooldown)),
            applyFallDamage: false);
    }

    private void OnStepTriggerAttempt(
        Entity<ZTransitionComponent> stairs,
        ref StepTriggerAttemptEvent args)
    {
        if (!TryComp(args.Tripper, out PhysicsComponent? physics) ||
            physics.BodyType == BodyType.Static ||
            Transform(args.Tripper).Anchored)
        {
            return;
        }

        args.Continue = true;
    }
}
