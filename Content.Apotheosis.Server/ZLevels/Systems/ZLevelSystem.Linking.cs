using Content.Apotheosis.Shared.ZLevels;
using Robust.Shared.Map.Components;

namespace Content.Apotheosis.Server.ZLevels;

public sealed partial class ZLevelSystem
{
    private void RebuildDirtyGroups()
    {
        if (_dirtyGroups.Count == 0)
            return;

        foreach (var group in _dirtyGroups)
            RebuildGroup(group);

        _dirtyGroups.Clear();
    }

    private void RebuildGroup(string group)
    {
        var levels = new Dictionary<int, Entity<ZLevelComponent>>();
        var expectedManaged = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ZLevelComponent, MapComponent>();

        while (query.MoveNext(out var uid, out var level, out _))
        {
            if (level.Group != group)
                continue;

            if (!levels.TryAdd(level.Level, (uid, level)))
                Log.Warning($"Z-level group '{group}' contains more than one map at level {level.Level}.");
        }

        foreach (var (_, upper) in levels)
        {
            if (!levels.TryGetValue(upper.Comp.Level - 1, out var lower))
            {
                RemoveManagedProjection(upper.Owner, group);
                continue;
            }

            LinkMapsCore(
                upper.Owner,
                lower.Owner,
                upper.Comp.BlurRadius,
                upper.Comp.RenderEntities,
                upper.Comp.ProjectBelow);

            var managed = EnsureComp<ZLevelManagedComponent>(upper.Owner);
            managed.Group = group;
            expectedManaged.Add(upper.Owner);
        }

        var managedQuery = EntityQueryEnumerator<ZLevelManagedComponent>();
        while (managedQuery.MoveNext(out var uid, out var managed))
        {
            if (managed.Group == group && !expectedManaged.Contains(uid))
                UnlinkLower(uid);
        }
    }

    private void RemoveManagedProjection(EntityUid uid, string group)
    {
        if (TryComp(uid, out ZLevelManagedComponent? managed) &&
            managed.Group == group)
        {
            UnlinkLower(uid);
        }
    }

    /// <summary>
    /// Links two maps as adjacent levels. Existing conflicting links are replaced.
    /// </summary>
    public bool LinkMaps(
        EntityUid upperMap,
        EntityUid lowerMap,
        float blurRadius = 1.5f,
        bool renderEntities = true)
    {
        if (!LinkMapsCore(upperMap, lowerMap, blurRadius, renderEntities))
            return false;

        RemComp<ZLevelManagedComponent>(upperMap);
        return true;
    }

    private bool LinkMapsCore(
        EntityUid upperMap,
        EntityUid lowerMap,
        float blurRadius,
        bool renderEntities,
        bool projectBelow = true)
    {
        if (upperMap == lowerMap ||
            !HasComp<MapComponent>(upperMap) ||
            !HasComp<MapComponent>(lowerMap) ||
            WouldCreateCycle(upperMap, lowerMap))
        {
            return false;
        }

        if (TryComp(upperMap, out ZLevelLinkComponent? upperLink) &&
            upperLink.LowerMap is { } oldLower &&
            oldLower != lowerMap)
        {
            UnlinkLower(upperMap);
        }

        if (TryComp(lowerMap, out ZLevelLinkComponent? lowerLink) &&
            lowerLink.UpperMap is { } oldUpper &&
            oldUpper != upperMap)
        {
            UnlinkLower(oldUpper);
        }

        upperLink = EnsureComp<ZLevelLinkComponent>(upperMap);
        lowerLink = EnsureComp<ZLevelLinkComponent>(lowerMap);
        upperLink.LowerMap = lowerMap;
        lowerLink.UpperMap = upperMap;
        Dirty(upperMap, upperLink);
        Dirty(lowerMap, lowerLink);

        if (projectBelow)
        {
            var projection = EnsureComp<MapProjectionComponent>(upperMap);
            blurRadius = Math.Clamp(blurRadius, 0f, 8f);

            if (projection.SourceMap != lowerMap ||
                projection.BlurRadius != blurRadius ||
                projection.RenderEntities != renderEntities)
            {
                projection.SourceMap = lowerMap;
                projection.BlurRadius = blurRadius;
                projection.RenderEntities = renderEntities;
                Dirty(upperMap, projection);
            }
        }
        else
        {
            RemComp<MapProjectionComponent>(upperMap);
        }

        var linkChanged = new ZLevelLinkChangedEvent(lowerMap);
        RaiseLocalEvent(ref linkChanged);

        return true;
    }

    private bool WouldCreateCycle(EntityUid upperMap, EntityUid lowerMap)
    {
        var current = lowerMap;

        for (var depth = 0; depth < 64; depth++)
        {
            if (current == upperMap)
                return true;

            if (!TryComp(current, out ZLevelLinkComponent? link) ||
                link.LowerMap is not { } next)
            {
                return false;
            }

            current = next;
        }

        return true;
    }

    /// <summary>
    /// Removes the lower link from a map while preserving its own upper link.
    /// </summary>
    public bool UnlinkLower(EntityUid upperMap)
    {
        if (!TryComp(upperMap, out ZLevelLinkComponent? upperLink) ||
            upperLink.LowerMap is not { } lowerMap)
        {
            RemComp<MapProjectionComponent>(upperMap);
            RemComp<ZLevelManagedComponent>(upperMap);
            return false;
        }

        upperLink.LowerMap = null;
        if (upperLink.UpperMap == null)
            RemComp<ZLevelLinkComponent>(upperMap);
        else
            Dirty(upperMap, upperLink);

        if (TryComp(lowerMap, out ZLevelLinkComponent? lowerLink) &&
            lowerLink.UpperMap == upperMap)
        {
            lowerLink.UpperMap = null;
            if (lowerLink.LowerMap == null)
                RemComp<ZLevelLinkComponent>(lowerMap);
            else
                Dirty(lowerMap, lowerLink);
        }

        RemComp<MapProjectionComponent>(upperMap);
        RemComp<ZLevelManagedComponent>(upperMap);

        var linkChanged = new ZLevelLinkChangedEvent(lowerMap);
        RaiseLocalEvent(ref linkChanged);
        return true;
    }
}
