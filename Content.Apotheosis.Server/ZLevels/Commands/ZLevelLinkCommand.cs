using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Apotheosis.Server.ZLevels;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ZLevelLinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "zlevel-link";
    public string Description => "Links two maps as adjacent Z-levels.";
    public string Help =>
        "Usage: zlevel-link <upper map/grid uid> <lower map/grid uid> [darkness 0..1] [render entities true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteError(Help);
            return;
        }

        if (!ZLevelCommandUtility.TryResolveMap(_entities, args[0], out var upperMap))
        {
            shell.WriteError("Upper UID does not belong to a valid map.");
            return;
        }

        if (!ZLevelCommandUtility.TryResolveMap(_entities, args[1], out var lowerMap))
        {
            shell.WriteError("Lower UID does not belong to a valid map.");
            return;
        }

        var darkness = 0.45f;
        if (args.Length >= 3 &&
            (!float.TryParse(args[2], out darkness) || darkness is < 0f or > 1f))
        {
            shell.WriteError("Darkness must be a number from 0 to 1.");
            return;
        }

        var renderEntities = true;
        if (args.Length == 4 && !bool.TryParse(args[3], out renderEntities))
        {
            shell.WriteError("Render entities must be true or false.");
            return;
        }

        if (!_entities.System<ZLevelSystem>().LinkMaps(
                upperMap,
                lowerMap,
                darkness,
                renderEntities))
        {
            shell.WriteError("The maps could not be linked.");
            return;
        }

        shell.WriteLine(
            $"Map {upperMap} is now above map {lowerMap}; darkness is {darkness:0.##}; " +
            $"lower objects are {(renderEntities ? "visible" : "hidden")}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 or 2 => ZLevelCommandUtility.Maps(_entities),
            3 => CompletionResult.FromHint("<darkness 0..1>"),
            4 => CompletionResult.FromHintOptions(["true", "false"], "<render entities>"),
            _ => CompletionResult.Empty,
        };
    }
}
