using Content.Trauma.Shared.Heretic.Components.PathSpecific.Cosmos;
using Content.Trauma.Shared.Heretic.Systems.PathSpecific.Cosmos;

namespace Content.Trauma.Server.Heretic.Systems.PathSpecific;

public sealed partial class StarMarkSystem : SharedStarMarkSystem
{
    protected override void InitializeCosmicField(Entity<CosmicFieldComponent> field, int strength)
    {
        base.InitializeCosmicField(field, strength);
    }
}

