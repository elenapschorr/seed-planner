namespace SeedPlanner.Domain;

/// <summary>How long a plant type lives in a garden here.</summary>
public enum Lifecycle
{
    Annual,
    Biennial,
    Perennial,

    /// <summary>Perennial, but only with protection over the winter (e.g. tomato, pepper, eggplant).</summary>
    ConditionallyPerennial
}

/// <summary>What the user has of a variety: seed, a young plant or a cutting.</summary>
public enum Propagation
{
    Seed,
    Plant,
    Cutting
}
