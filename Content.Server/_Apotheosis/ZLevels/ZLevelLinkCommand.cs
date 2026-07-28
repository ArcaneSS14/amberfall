using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Apotheosis.ZLevels;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ZLevelLinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "zlevel-link";
    public string Description => "Links two grids or maps as adjacent Z-levels.";
    public string Help =>
        "Usage: zlevel-link <upper grid/map uid> <lower grid/map uid> [darkness 0..1] [render entities true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryResolveGrid(args[0], out var upper))
        {
            shell.WriteError("Upper UID is not a valid grid or map with a grid.");
            return;
        }

        if (!TryResolveGrid(args[1], out var lower))
        {
            shell.WriteError("Lower UID is not a valid grid or map with a grid.");
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

        if (!_entities.System<ApotheosisZLevelSystem>().LinkGrids(
                upper,
                lower,
                darkness,
                renderEntities))
        {
            shell.WriteError("The grids could not be linked.");
            return;
        }

        shell.WriteLine(
            $"Grid {upper} is now above grid {lower}; darkness is {darkness:0.##}; " +
            $"lower objects are {(renderEntities ? "visible" : "hidden")}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 or 2 => ZLevelCommandCompletion.Grids(_entities),
            3 => CompletionResult.FromHint("<darkness 0..1>"),
            4 => CompletionResult.FromHintOptions(["true", "false"], "<render entities>"),
            _ => CompletionResult.Empty,
        };
    }

    private bool TryResolveGrid(string value, out EntityUid gridUid)
    {
        gridUid = default;
        if (!EntityUid.TryParse(value, out var uid))
            return false;

        if (_entities.HasComponent<MapGridComponent>(uid))
        {
            gridUid = uid;
            return true;
        }

        if (!_entities.TryGetComponent(uid, out MapComponent? map))
            return false;

        var selected = _entities.System<SharedMapSystem>()
            .GetAllGrids(map.MapId)
            .OrderByDescending(grid => Box2.Area(grid.Comp.LocalAABB))
            .FirstOrDefault();

        if (selected.Owner == default)
            return false;

        gridUid = selected.Owner;
        return true;
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ZLevelUnlinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "zlevel-unlink";
    public string Description => "Removes the lower Z-level link from a grid.";
    public string Help => "Usage: zlevel-unlink <upper grid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !TryResolveGrid(args[0], out var upper))
        {
            shell.WriteError(Help);
            return;
        }

        _entities.System<ApotheosisZLevelSystem>().UnlinkLower(upper);
        shell.WriteLine($"Grid {upper} no longer has a lower Z-level.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? ZLevelCommandCompletion.Grids(_entities)
            : CompletionResult.Empty;
    }

    private bool TryResolveGrid(string value, out EntityUid gridUid)
    {
        gridUid = default;
        if (!EntityUid.TryParse(value, out var uid))
            return false;

        if (_entities.HasComponent<MapGridComponent>(uid))
        {
            gridUid = uid;
            return true;
        }

        if (!_entities.TryGetComponent(uid, out MapComponent? map))
            return false;

        var selected = _entities.System<SharedMapSystem>()
            .GetAllGrids(map.MapId)
            .OrderByDescending(grid => Box2.Area(grid.Comp.LocalAABB))
            .FirstOrDefault();

        if (selected.Owner == default)
            return false;

        gridUid = selected.Owner;
        return true;
    }
}

internal static class ZLevelCommandCompletion
{
    public static CompletionResult Grids(IEntityManager entities)
    {
        var options = new List<CompletionOption>();
        var query = entities.EntityQueryEnumerator<MapGridComponent, TransformComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out _, out var transform, out var metadata))
        {
            options.Add(new CompletionOption(
                uid.ToString(),
                $"{metadata.EntityName} — map {transform.MapID}"));
        }

        options.Sort();
        return CompletionResult.FromHintOptions(options, "<grid UID>");
    }
}
