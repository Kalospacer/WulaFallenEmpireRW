using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_EnergyLanceTurret : CompProperties
    {
        // 光束配置
        public ThingDef energyLanceDef;                    // EnergyLance 定义
        public int energyLanceDuration = 600;              // 光束持续时间
        public float energyLanceMoveDistance = 15f;        // 光束移动距离
        
        // 目标检测配置
        public float detectionRange = 30f;                 // 检测范围
        
        // 使用现有的 RimWorld 目标类型
        public bool targetHostileFactions = true;          // 目标敌对派系
        public bool targetNeutrals = false;                // 目标中立单位
        public bool targetAnimals = false;                 // 目标动物
        public bool targetMechs = true;                    // 目标机械单位
        public bool requireLineOfSight = false;             // 需要视线
        
        // 锁定配置
        public int targetUpdateInterval = 60;              // 目标更新间隔（ticks）
        public float targetSwitchRange = 25f;              // 切换目标的最大距离
        
        // 光束生成配置
        public int warmupTicks = 30;                       // 预热时间
        public int cooldownTicks = 120;                    // 冷却时间
        
        public CompProperties_EnergyLanceTurret()
        {
            compClass = typeof(CompEnergyLanceTurret);
        }
    }
}
