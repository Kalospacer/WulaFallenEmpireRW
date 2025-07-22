using RimWorld;
using Verse;
using Verse.AI;
using System.Linq; // For FirstOrDefault

namespace WulaFallenEmpire
{
    public class JobGiver_WulaGetEnergy : ThinkNode_JobGiver
    {
        public float minEnergyLevelPercentage = 0.3f;
        public float emergencyThreshold = 0.1f;
        public float normalPriority = 5f;
        public float emergencyPriority = 9.5f;
        public float searchRadius = 20f; // 添加 searchRadius
        public int ingestCount = 1; // 添加 ingestCount

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_WulaGetEnergy obj = (JobGiver_WulaGetEnergy)base.DeepCopy(resolve);
            obj.minEnergyLevelPercentage = minEnergyLevelPercentage;
            obj.emergencyThreshold = emergencyThreshold;
            obj.normalPriority = normalPriority;
            obj.emergencyPriority = emergencyPriority;
            obj.searchRadius = searchRadius;
            obj.ingestCount = ingestCount;
            return obj;
        }

        public override float GetPriority(Pawn pawn)
        {
            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
            {
                return 0f;
            }

            // 如果能量充足，则不需要寻找能量核心
            if (energyNeed.CurLevelPercentage > minEnergyLevelPercentage)
            {
                return 0f;
            }

            // 如果能量非常低，给予高优先级
            if (energyNeed.CurLevelPercentage < emergencyThreshold)
            {
                return emergencyPriority;
            }
            
            // 否则，给予中等优先级
            return normalPriority;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Downed)
            {
                return null;
            }
            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null || energyNeed.CurLevelPercentage > minEnergyLevelPercentage)
            {
                return null;
            }

            // 优先检查小人背包中的能量核心
            foreach (Thing t in pawn.inventory.innerContainer)
            {
                ThingDefExtension_EnergySource energySourceExt = t.def.GetModExtension<ThingDefExtension_EnergySource>();
                if (energySourceExt != null && t.IngestibleNow)
                {
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy"), t);
                    job.count = ingestCount;
                    return job;
                }
            }

            // 如果背包中没有，则寻找最佳能量核心
            Thing bestEnergySource = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), // 扫描所有可搬运的物品
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false),
                searchRadius, // 使用类中的 searchRadius
                (Thing t) =>
                {
                    // 检查物品是否是能量核心
                    ThingDefExtension_EnergySource energySourceExt = t.def.GetModExtension<ThingDefExtension_EnergySource>();
                    if (energySourceExt == null)
                    {
                        return false;
                    }
                    // 检查物品是否可摄取
                    if (!t.IngestibleNow)
                    {
                        return false;
                    }
                    // 检查物品是否被禁止或无法预留
                    if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, false))
                    {
                        return false;
                    }
                    return true;
                }
            );

            if (bestEnergySource != null)
            {
                // 创建摄取能量核心的Job
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy"), bestEnergySource);
                job.count = ingestCount; // 使用类中的 ingestCount
                return job;
            }

            return null;
        }
    }
}
