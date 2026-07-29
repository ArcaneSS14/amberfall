using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Apotheosis.Server.ZLevels;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ZLevelUnlinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "zlevel-unlink";
    public string Description => "Removes the lower Z-level link from a map.";
    public string Help => "Usage: zlevel-unlink <upper map/grid uid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 ||
            !ZLevelCommandUtility.TryResolveMap(_entities, args[0], out var upperMap))
        {
            shell.WriteError(Help);
            return;
        }

        _entities.System<ZLevelSystem>().UnlinkLower(upperMap);
        shell.WriteLine($"Map {upperMap} no longer has a lower Z-level.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? ZLevelCommandUtility.Maps(_entities)
            : CompletionResult.Empty;
    }
}
