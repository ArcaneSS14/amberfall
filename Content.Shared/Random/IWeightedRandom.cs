using Robust.Shared.Prototypes;

namespace Content.Shared.Random;

/// <summary>
/// Associates entries with weights for random selection.
/// </summary>
public interface IWeightedRandomPrototype<T> : IPrototype where T : notnull
{
    [ViewVariables]
    public Dictionary<T, float> Weights { get; }
}
