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

/// <summary>Where the height values of a variety come from.</summary>
public enum HeightEvidence
{
    /// <summary>A source names this exact variety.</summary>
    Variety,

    /// <summary>Partial source or a similar variety of the same type.</summary>
    Similar,

    /// <summary>Typical value of the plant type, not checked for this variety.</summary>
    Species
}
