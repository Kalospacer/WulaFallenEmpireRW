using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_BuildingSpawner : CompProperties
    {
        public ThingDef buildingToSpawn;
        public bool destroyBuilding = true;
        public int delayTicks = 0;
        
        // 自动召唤设置
        public bool canAutoCall = true;
        public int autoCallDelayTicks = 0; // 默认10秒
        
        // FlyOver 前提条件
        public bool requireFlyOver = false;
        
        // 屋顶限制
        public bool allowThinRoof = true;
        public bool allowThickRoof = true;
        
        // 新增：科技需求
        public ResearchProjectDef requiredResearch;
        
        // 新增：生成时的效果器
        public EffecterDef spawnEffecter;
        
        // 新增：音效
        public SoundDef spawnSound;
        
        // 新增：建筑朝向设置
        public Rot4 buildingRotation = Rot4.North;
        
        // 新增：位置偏移
        public IntVec2 spawnOffset = IntVec2.Zero;
        
        // 新增：允许替换现有建筑
        public bool canReplaceExisting = false;
        
        // 新增：是否继承原建筑的派系
        public bool inheritFaction = true;
        
        // 新增：建筑生成后的燃料量（如果适用）
        public FloatRange fuelRange = new FloatRange(1f, 1f);
        
        // 新增：是否在口袋地图中跳过科技检查
        public bool skipResearchCheckInPocketMap = true;
        
        // 新增：是否在口袋地图中跳过FlyOver检查
        public bool skipFlyOverCheckInPocketMap = true;
        
        // 新增：是否在口袋地图中跳过屋顶检查
        public bool skipRoofCheckInPocketMap = true;
        
        public CompProperties_BuildingSpawner()
        {
            compClass = typeof(CompBuildingSpawner);
        }
        
        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            
            // 验证buildingToSpawn
            if (buildingToSpawn != null && buildingToSpawn.category != ThingCategory.Building)
            {
                Log.Error($"CompProperties_BuildingSpawner: buildingToSpawn must be a building, but got {buildingToSpawn.defName}");
                buildingToSpawn = null;
            }
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }
            
            if (buildingToSpawn == null)
            {
                yield return "buildingToSpawn is not set";
            }
            else if (buildingToSpawn.category != ThingCategory.Building)
            {
                yield return $"buildingToSpawn must be a building, but got {buildingToSpawn.defName}";
            }
        }
    }
}
