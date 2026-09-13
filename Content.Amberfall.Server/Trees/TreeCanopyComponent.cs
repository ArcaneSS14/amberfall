using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Amberfall;

/// <summary>
/// Builds a tree canopy on the linked map above the trunk.
/// </summary>
[RegisterComponent]
public sealed partial class TreeCanopyComponent : Component
{
    [DataField(required: true)]
    public EntProtoId ExtendPrototype;

    [DataField(required: true)]
    public EntProtoId EndPrototype;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> FoliageTile;

    [DataField]
    public float ExtendDistance = 1f;

    [DataField]
    public float EndDistance = 2f;

    public readonly List<EntityUid> SpawnedBranches = new();
    public readonly List<(EntityUid Grid, Vector2i Indices)> CanopyTiles = new();
}
