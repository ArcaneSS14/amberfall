using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Amberfall.Server.ZLevels;

internal static class ZLevelCommandUtility
{
    public static bool TryResolveMap(
        IEntityManager entities,
        string value,
        out EntityUid mapUid)
    {
        mapUid = default;
        if (!EntityUid.TryParse(value, out var uid))
            return false;

        if (entities.HasComponent<MapComponent>(uid))
        {
            mapUid = uid;
            return true;
        }

        if (!entities.TryGetComponent(uid, out TransformComponent? transform) ||
            transform.MapUid is not { } map ||
            !entities.HasComponent<MapComponent>(map))
        {
            return false;
        }

        mapUid = map;
        return true;
    }

    public static CompletionResult Maps(IEntityManager entities)
    {
        var options = new List<CompletionOption>();
        var query = entities.EntityQueryEnumerator<MapComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out var map, out var metadata))
        {
            options.Add(new CompletionOption(
                uid.ToString(),
                $"{metadata.EntityName} — map {map.MapId}"));
        }

        options.Sort();
        return CompletionResult.FromHintOptions(options, "<map UID>");
    }
}
