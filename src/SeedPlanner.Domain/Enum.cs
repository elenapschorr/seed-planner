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
public enum PlantCategory
{
    FruitVegetable, RootVegetable, LeafyVegetable, StemVegetable, Brassica, Legume,
    Allium, Melon, Herb, Berry, FruitTree, Flower
}
public enum NutrientDemand { Heavy, Medium, Light }
public enum DemandUnit { Plants, RowMeters, SquareMeters }
