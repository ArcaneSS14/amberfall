using Robust.Shared.GameStates;

namespace Content.Amberfall.Shared.ZLevels;

/// <summary>
/// Runtime links to the maps immediately above and below this map.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelLinkComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? UpperMap;

    [AutoNetworkedField]
    public EntityUid? LowerMap;
}
