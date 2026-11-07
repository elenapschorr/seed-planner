namespace SeedPlanner.Domain;

public enum SeedType { OpenPollinated, F1Hybrid }
public enum StockStatus { InStock, OutOfStock, Retired }
public enum CultivationMethod { StartIndoors, DirectSow, Container, ColdFrame }
public enum GerminationType { Light, Dark, Vegetative }
public enum Exposure { FullSun, PartialShade, Shade }
public enum WindowKind { Sowing, Harvest }
public enum PlantCategory
{
    FruitVegetable, RootVegetable, LeafyVegetable, StemVegetable, Brassica, Legume,
    Allium, Melon, Herb, Berry, FruitTree, Flower
}
public enum NutrientDemand { Heavy, Medium, Light }
public enum DemandUnit { Plants, RowMeters, SquareMeters }