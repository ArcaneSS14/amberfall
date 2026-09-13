using Robust.Shared.Prototypes;
using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;

namespace Content.Shared.NukeOps;

/// <summary>
/// This is used for tagging a mob as a nuke operative.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NukeOperativeComponent : Component
{

    /// <summary>
    ///
    /// </summary>
    [DataField("syndStatusIcon")]
    public ProtoId<FactionIconPrototype> SyndStatusIcon = "SyndicateFaction";
}
