namespace Content.Shared.Maps;

public sealed partial class ContentTileDefinition
{
    /// <summary>
    /// Renders a linked lower Z-level below this tile. Intended for transparent floors.
    /// </summary>
    [DataField]
    public bool RenderZLevelBelow { get; private set; }

    /// <summary>
    /// Makes moving physical entities fall through this tile to a linked lower map.
    /// Empty tiles always allow falling regardless of this value.
    /// </summary>
    [DataField]
    public bool FallThroughZLevel { get; private set; }
}
