// CompProperties_ProductionCategory.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public enum ProductionCategory
    {
        Equipment,  // 装备
        Weapon,     // 武器
        Mechanoid   // 机械体
    }

    public class CompProperties_ProductionCategory : CompProperties
    {
        public ProductionCategory category = ProductionCategory.Equipment;

        public CompProperties_ProductionCategory()
        {
            this.compClass = typeof(CompProductionCategory);
        }
    }

    public class CompProductionCategory : ThingComp
    {
        public CompProperties_ProductionCategory Props => (CompProperties_ProductionCategory)props;

        public ProductionCategory Category => Props.category;
    }
}
