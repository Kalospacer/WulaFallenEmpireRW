using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_BuildingBombardment : CompProperties
    {
        // 轰炸区域配置
        public float radius = 15f;                  // 作用半径
        
        // 目标选择配置
        public bool targetEnemies = true;           // 是否以敌人为目标
        public bool targetNeutrals = false;         // 是否以中立单位为目标
        public bool targetAnimals = false;          // 是否以动物为目标
        
        // 召唤配置
        public int burstCount = 3;                  // 单组召唤数量
        public int innerBurstIntervalTicks = 10;    // 同组召唤间隔
        public int burstIntervalTicks = 60;         // 组间召唤间隔
        public float randomOffset = 2f;             // 随机偏移量
        
        // Skyfaller 配置
        public ThingDef skyfallerDef;               // 使用的 Skyfaller
        public ThingDef projectileDef;              // 备用的抛射体定义
        
        public CompProperties_BuildingBombardment()
        {
            this.compClass = typeof(CompBuildingBombardment);
        }
    }
}
