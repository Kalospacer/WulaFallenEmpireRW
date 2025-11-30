using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_SkyfallerCaller : CompProperties
    {
        public ThingDef skyfallerDef;
        public bool destroyBuilding = true;
        public int delayTicks = 0;

        public bool canAutoCall = true; // 默认启用自动召唤
        public int autoCallDelayTicks = 1; // 默认10秒
        
        // 新增：是否需要 FlyOver 作为前提条件
        public bool requireFlyOver = false; // 默认不需要 FlyOver
        
        public bool allowThinRoof = true; // 允许砸穿薄屋顶
        public bool allowThickRoof = false; // 是否允许在厚岩顶下空投
        
        public CompProperties_SkyfallerCaller()
        {
            compClass = typeof(CompSkyfallerCaller);
        }
    }
}
