using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_BuildingEnergyLance : CompProperties
    {
        // 基础配置
        public float radius = 20f;                        // 作用半径
        public int updateIntervalTicks = 30;              // 目标更新间隔（刻）
        public int maxNoTargetTicks = 120;                // 无目标时最大存活时间
        
        // 目标选择配置
        public bool targetEnemies = true;                 // 是否以敌人为目标
        public bool targetNeutrals = false;               // 是否以中立单位为目标
        public bool targetAnimals = false;                // 是否以动物为目标
        
        // EnergyLance配置
        public ThingDef energyLanceDef;                   // 使用的EnergyLance类型
        public int energyLanceDuration = 600;             // EnergyLance基础持续时间
        public int firesPerTick = 3;                      // 每刻伤害次数
        
        public CompProperties_BuildingEnergyLance()
        {
            this.compClass = typeof(CompBuildingEnergyLance);
        }
    }
}
