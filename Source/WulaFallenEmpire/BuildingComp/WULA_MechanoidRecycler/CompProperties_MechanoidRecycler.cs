using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // 初始单位配置类
    public class InitialUnitConfig
    {
        public PawnKindDef pawnKindDef;
        public int count = 1;
    }
    
    public class CompProperties_MechanoidRecycler : CompProperties
    {
        // 回收相关
        public List<ThingDef> recyclableRaces = new List<ThingDef>();
        public int recycleRange = 15;
        public JobDef recycleJobDef;
        public int maxStorageCapacity = 5;
        
        // 生成相关
        public List<PawnKindDef> spawnablePawnKinds = new List<PawnKindDef>();
        
        // 初始单位配置
        public List<InitialUnitConfig> initialUnits = new List<InitialUnitConfig>();
        
        // 归属权配置
        public Faction ownershipFaction = null; // 如果为null，则默认使用玩家派系
        
        public CompProperties_MechanoidRecycler()
        {
            compClass = typeof(CompMechanoidRecycler);
        }
    }
    
    // 空的组件类，用于属性存储
    public class CompMechanoidRecycler : ThingComp
    {
        // 组件逻辑主要在建筑类中实现
    }
}
