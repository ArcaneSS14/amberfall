using System.Numerics;
using Content.Shared._Apotheosis.ZLevels;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Apotheosis.ZLevels;

/// <summary>
/// Handles falling through empty upper tiles and explicit stair transitions.
/// </summary>
public sealed partial class ApotheosisZTransitionSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _transitioning = new();
    private static readonly TimeSpan FallCooldown = TimeSpan.FromSeconds(0.25);

    public override void Initialize()
    {
        base.Initialize();

        _transform.OnGlobalMoveEvent += OnMove;
        SubscribeLocalEvent<ApotheosisZTransitionComponent, StepTriggeredOffEvent>(OnStepTriggered);
        SubscribeLocalEvent<ApotheosisZTransitionComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
    }

    public override void Shutdown()
    {
        _transform.OnGlobalMoveEvent -= OnMove;
        base.Shutdown();
    }

    private void OnMove(ref MoveEvent args)
    {
        var uid = args.Entity.Owner;
        if (args.OnlyRotation ||
            args.Component.Anchored ||
            !TryComp(uid, out PhysicsComponent? physics) ||
            physics.BodyType == BodyType.Static ||
            !CanTransition(uid))
        {
            return;
        }

        EntityUid sourceGrid;
        if (HasComp<ApotheosisZLinkComponent>(args.NewPosition.EntityId))
        {
            sourceGrid = args.NewPosition.EntityId;
        }
        else if (args.ParentChanged &&
                 HasComp<ApotheosisZLinkComponent>(args.OldPosition.EntityId))
        {
            sourceGrid = args.OldPosition.EntityId;
        }
        else
        {
            return;
        }

        if (!TryComp(sourceGrid, out ApotheosisZLinkComponent? link) ||
            link.LowerGrid is not { } lowerGrid ||
            !TryComp(sourceGrid, out MapGridComponent? sourceGridComponent))
        {
            return;
        }

        var localPosition = _transform.GetRelativePosition(args.Component, sourceGrid);
        var sourceCoordinates = new EntityCoordinates(sourceGrid, localPosition);
        var tile = _map.GetTileRef(sourceGrid, sourceGridComponent, sourceCoordinates);
        if (!CanFallThrough(tile.Tile))
            return;

        var destination = FindLandingGrid(lowerGrid, localPosition);
        Transition(uid, destination, localPosition, Vector2.Zero, FallCooldown);
    }

    private EntityUid FindLandingGrid(EntityUid firstLowerGrid, Vector2 localPosition)
    {
        var destination = firstLowerGrid;
        var visited = new HashSet<EntityUid>();

        while (visited.Add(destination) &&
               TryComp(destination, out MapGridComponent? grid))
        {
            var coordinates = new EntityCoordinates(destination, localPosition);
            if (!CanFallThrough(_map.GetTileRef(destination, grid, coordinates).Tile))
                break;

            if (!TryComp(destination, out ApotheosisZLinkComponent? link) ||
                link.LowerGrid is not { } nextLower)
            {
                break;
            }

            destination = nextLower;
        }

        return destination;
    }

    private bool CanFallThrough(Tile tile)
    {
        return tile.IsEmpty || _tiles[tile.TypeId].FallThroughZLevel;
    }

    private void OnStepTriggered(
        Entity<ApotheosisZTransitionComponent> stairs,
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
        if (stairsTransform.GridUid is not { } sourceGrid ||
            !TryComp(sourceGrid, out ApotheosisZLinkComponent? link))
        {
            return;
        }

        var targetGrid = stairs.Comp.Direction switch
        {
            ApotheosisZDirection.Up => link.UpperGrid,
            ApotheosisZDirection.Down => link.LowerGrid,
            _ => null,
        };

        if (targetGrid is not { } destination || !HasComp<MapGridComponent>(destination))
            return;

        var tripperTransform = Transform(args.Tripper);
        if (tripperTransform.GridUid != sourceGrid)
            return;

        var localPosition = tripperTransform.Coordinates.Position;
        var cooldown = TimeSpan.FromSeconds(Math.Max(0f, stairs.Comp.Cooldown));
        Transition(args.Tripper,
            destination,
            localPosition,
            stairs.Comp.DestinationOffset,
            cooldown);
    }

    private void OnStepTriggerAttempt(
        Entity<ApotheosisZTransitionComponent> stairs,
        ref StepTriggerAttemptEvent args)
    {
        if (!TryComp(args.Tripper, out PhysicsComponent? physics) ||
            physics.BodyType == BodyType.Static ||
            Transform(args.Tripper).Anchored)
            return;

        args.Continue = true;
    }

    private bool CanTransition(EntityUid uid)
    {
        if (_transitioning.Contains(uid))
            return false;

        if (!TryComp(uid, out ApotheosisZTransitionCooldownComponent? cooldown))
            return true;

        if (cooldown.Until > _timing.CurTime)
            return false;

        RemCompDeferred(uid, cooldown);
        return true;
    }

    private void Transition(
        EntityUid uid,
        EntityUid destinationGrid,
        Vector2 localPosition,
        Vector2 offset,
        TimeSpan cooldown)
    {
        if (!Exists(destinationGrid))
            return;

        var cooldownComponent = EnsureComp<ApotheosisZTransitionCooldownComponent>(uid);
        cooldownComponent.Until = _timing.CurTime + cooldown;

        _transitioning.Add(uid);
        try
        {
            _transform.SetCoordinates(uid,
                new EntityCoordinates(destinationGrid, localPosition + offset));
        }
        finally
        {
            _transitioning.Remove(uid);
        }
    }
}
