namespace SeedPlanner.Domain;

public enum SeedType { OpenPollinated, F1Hybrid }
public enum StockStatus { InStock, OutOfStock, Retired }
public enum CultivationMethod { StartIndoors, DirectSow, Container, ColdFrame }
public enum GerminationType { Light, Dark, Vegetative }
public enum Exposure { FullSun, PartialShade, Shade }

/// <summary>
/// The kinds of container a plant can be put into. A bed is measured in m², all the others in litres.
/// The pot kinds are those of <see cref="PotKind"/>.
/// </summary>
public enum ContainerCategory { Bed, Pot, Bowl, GrowBag }

/// <summary>Kind of soil: what a plant type likes and what a pot or bed contains.</summary>
public enum SoilType { Sandy, Loamy, Clay, Humus, PottingSoil }
public enum WindowKind { Sowing, Harvest, Bloom }

/// <summary>
/// When within the sowing window a variety is sown: always at the start, in the middle or at the end of it, or the
/// distribution chooses (the earliest date at which a place is free). The end of the harvest stays as entered.
/// </summary>
public enum SowingTiming { Start, Middle, End, Auto }
public enum PlantCategory
{
    FruitVegetable, RootVegetable, LeafyVegetable, StemVegetable, Brassica, Legume,
    Allium, Melon, Herb, Berry, FruitTree, Flower
}
public enum NutrientDemand { Heavy, Medium, Light }
public enum DemandUnit { Plants, RowMeters, SquareMeters }

/// <summary>Main plant: big, tall, stands long. Secondary plant: lower, fits in between or below a main plant.</summary>
public enum PlantRole { Main, Secondary }
