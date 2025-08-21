using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld; // For JobDefOf, ThingDefOf, StatDefOf

namespace WulaFallenEmpire
{
    public class JobGiver_WulaPackEnergy : ThinkNode_JobGiver
    {
        public float packEnergyThreshold = 0.5f; // 默认打包能量阈值
        public int packEnergyCount = 2; // 默认打包数量

        // 定义乌拉能量核心的ThingDef
        private static ThingDef WULA_Charge_Cube_Def => ThingDef.Named("WULA_Charge_Cube");

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_WulaPackEnergy obj = (JobGiver_WulaPackEnergy)base.DeepCopy(resolve);
            obj.packEnergyThreshold = packEnergyThreshold;
            obj.packEnergyCount = packEnergyCount;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.inventory == null)
            {
                return null;
            }

            // 检查背包中是否有足够的能量核心，这里可以根据Need_WulaEnergy的当前值来判断是否需要打包
            // 简化逻辑：如果能量低于某个阈值，并且背包中没有能量核心，则尝试打包
            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
            {
                return null;
            }

            // 只有当能量低于阈值，并且背包中能量核心数量少于2个时，才尝试打包
            if (energyNeed.CurLevelPercentage > packEnergyThreshold || pawn.inventory.innerContainer.TotalStackCountOfDef(WULA_Charge_Cube_Def) >= 2)
            {
                return null;
            }

            // 检查是否超重
            if (MassUtility.IsOverEncumbered(pawn))
            {
                return null;
            }

            // 寻找地图上可触及的WULA_Charge_Cube
            Thing thing = GenClosest.ClosestThing_Regionwise_ReachablePrioritized(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(WULA_Charge_Cube_Def), // 只寻找WULA_Charge_Cube
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                20f, // 搜索距离
                delegate(Thing t)
                {
                    // 检查物品是否被禁止，是否可预留，是否社交得体
                    return !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.IsSociallyProper(pawn);
                },
                (Thing x) => 0f // 优先级，这里可以根据距离或其他因素调整
            );

            if (thing == null)
            {
                return null;
            }

            // 计算需要打包的数量，限制在1到2个
            int countToTake = Mathf.Min(thing.stackCount, 2); // 限制为最多2个
            if (WULA_Charge_Cube_Def != null)
            {
                countToTake = Mathf.Min(countToTake, WULA_Charge_Cube_Def.stackLimit);
            }
            
            // 创建TakeInventory Job
            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, thing);
            job.count = countToTake;
            return job;
        }
    }
}
