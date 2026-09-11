namespace Content.Apotheosis.Shared.ZLevels;

/// <summary>
/// Projects nearby tiles and sprites from another map into this map's coordinate space.
/// Physics, lighting, occlusion, and simulation remain isolated between maps.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelProjectionComponent : Component
{
    /// <summary>
    /// Map whose visible grids are sampled in map-space coordinates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? SourceMap;

    /// <summary>
    /// Blur sample offset applied to the projected map, in main viewport pixels.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BlurRadius = 1.5f;

    /// <summary>
    /// Whether nearby entities on the source map should also be projected.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RenderEntities = true;
}

