using Content.Apotheosis.Shared.ZLevels;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Map;

namespace Content.Apotheosis.Server.ZLevels;

/// <summary>
/// Handles falling through maps and explicit stair transitions.
/// </summary>
public sealed partial class ZTransitionSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        _transform.OnGlobalMoveEvent += OnMove;
        SubscribeLocalEvent<ZTransitionComponent, StepTriggeredOffEvent>(OnStepTriggered);
        SubscribeLocalEvent<ZTransitionComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
    }

    public override void Shutdown()
    {
        _transform.OnGlobalMoveEvent -= OnMove;
        base.Shutdown();
    }
}
