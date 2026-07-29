namespace Content.Apotheosis.Shared.ZLevels;

/// <summary>
/// Raised after an entity moves between linked maps.
/// </summary>
public readonly record struct ZLevelTransitionedEvent(
    EntityUid Entity,
    EntityUid SourceMap,
    EntityUid DestinationMap,
    Vector2 MapPosition);
