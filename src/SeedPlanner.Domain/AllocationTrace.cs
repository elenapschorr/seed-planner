namespace SeedPlanner.Domain;

/// <summary>What happened in the distribution, in the order it happened. The page turns a code and its values into text.</summary>
public enum TraceCode
{
    /// <summary>A plant in the order in which the plants are placed (Count = position).</summary>
    PlantOrder,
    PlantOrderColdFrame,
    /// <summary>A plant that was left out before the distribution started (Detail = reason).</summary>
    Excluded,
    /// <summary>No chosen bed has a cover: an existing bed gets one (Container, Value = area of the cold frame plants).</summary>
    CoverRetrofit,
    /// <summary>No chosen bed has a cover: new covered beds (Count = number, Value = area each).</summary>
    NewCoveredBeds,
    PhaseMain,
    PhasePotFallback,
    PhaseSecondaryAlone,
    PhaseCompanions,
    PhaseShortage,
    MainPlaced,
    /// <summary>Placed, and the sowing was moved later within its window.</summary>
    MainPlacedMoved,
    /// <summary>Placed in a container that had been empty: no container in use had room at any time of the window.</summary>
    MainPlacedEmptyBox,
    MainNotPlaced,
    CompanionPlaced,
    CompanionNotPlaced,
    /// <summary>A new pot (Value = litres).</summary>
    NewPot,
    /// <summary>A new bed (Value = area).</summary>
    NewBed,
    NewBedCovered,
    /// <summary>Plants were taken away in proportion (Count = plants removed, Value = share kept).</summary>
    ShrinkApplied
}

/// <summary>Why a container did not take a plant.</summary>
public enum RejectCode
{
    BedTooNarrow,
    SoilPh,
    CategoryNotChosen,
    TreeAndVegetable,
    NeedsCover,
    WrongKind,
    CoveredBed,
    NoSizeData,
    MainTaken,
    NoRoom,
    BadNeighbour,
    NoMainPlant,
    TooTall
}

/// <summary>How many of the containers were rejected for a reason.</summary>
public record TraceReason(RejectCode Code, int Count);

public record TraceEntry(
    TraceCode Code,
    int PlantId = 0,
    ContainerKind? Kind = null,
    string Container = "",
    bool IsNew = false,
    ulong Slots = 0,
    int Count = 1,
    double Value = 0,
    string Detail = "",
    IReadOnlyList<TraceReason>? Reasons = null);

/// <summary>Collects the entries of one run; plants placed one after the other in the same way become one entry.</summary>
internal sealed class TraceLog
{
    public List<TraceEntry> Entries { get; } = new();

    public void Add(TraceEntry e)
    {
        if (Entries.Count > 0 && e.Code != TraceCode.PlantOrder && e.Code != TraceCode.PlantOrderColdFrame)
        {
            var last = Entries[^1];
            if (last.Code == e.Code && last.PlantId == e.PlantId && last.Kind == e.Kind && last.Container == e.Container
                && last.IsNew == e.IsNew && last.Slots == e.Slots && last.Value == e.Value && last.Detail == e.Detail
                && SameReasons(last.Reasons, e.Reasons))
            {
                Entries[^1] = last with { Count = last.Count + e.Count };
                return;
            }
        }
        Entries.Add(e);
    }

    private static bool SameReasons(IReadOnlyList<TraceReason>? a, IReadOnlyList<TraceReason>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.SequenceEqual(b);
    }
}
