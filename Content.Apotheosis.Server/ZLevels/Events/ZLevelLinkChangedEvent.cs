namespace Content.Apotheosis.Server.ZLevels;

/// <summary>
/// Raised after a map gains, loses, or changes its link to the map above it.
/// </summary>
[ByRefEvent]
public readonly record struct ZLevelLinkChangedEvent(EntityUid LowerMap);
