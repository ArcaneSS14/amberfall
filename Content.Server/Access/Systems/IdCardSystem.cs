// <Trauma>
// </Trauma>
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server.Access.Systems;

public sealed partial class IdCardSystem : SharedIdCardSystem
{
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

    }

    public override void ExpireId(Entity<ExpireIdCardComponent> ent)
    {
        if (ent.Comp.Expired)
            return;

        base.ExpireId(ent);

        if (ent.Comp.ExpireMessage != null)
        {
            _chat.TrySendInGameICMessage(
                ent,
                Loc.GetString(ent.Comp.ExpireMessage),
                Shared.Chat.InGameICChatType.Speak,
                ChatTransmitRange.Normal,
                true);
        }
    }
}
