namespace SeedPlanner.Domain;

public enum SeedType { OpenPollinated, F1Hybrid }
public enum StockStatus { InStock, OutOfStock, Retired }
public enum CultivationMethod { StartIndoors, DirectSow, Container, ColdFrame }
public enum GerminationType { Light, Dark, Vegetative }
public enum Exposure { FullSun, PartialShade, Shade }

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
