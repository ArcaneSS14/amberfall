namespace Content.Amberfall.Shared.ZLevels;

/// <summary>
/// Projects nearby tiles and sprites from another map into this map's coordinate space.
/// Physics, lighting, occlusion, and simulation remain isolated between maps.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelProjectionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? SourceMap;

    [DataField, AutoNetworkedField]
    public float BlurRadius = 1.5f;

    [DataField, AutoNetworkedField]
    public bool RenderEntities = true;
}

