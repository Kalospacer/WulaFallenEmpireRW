using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_WulaGetEnergy : ThinkNode_JobGiver
    {
        public float minEnergyLevelPercentage = 0.3f;
        public float maxEnergyLevelPercentage = 0.9f;
        public float emergencyPriority = 9.5f;

        public override float GetPriority(Pawn pawn)
        {
            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null || pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                return 0f;
            }

            if (energyNeed.CurLevelPercentage < minEnergyLevelPercentage)
            {
                return emergencyPriority;
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                return null;
            }

            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null || energyNeed.CurLevelPercentage >= maxEnergyLevelPercentage)
            {
                return null;
            }

            if (!TryFindBestEnergySourceFor(pawn, out Thing energySource))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy"), energySource);
            job.count = 1;
            return job;
        }

        private bool TryFindBestEnergySourceFor(Pawn pawn, out Thing energySource)
        {
            // 优先从背包中寻找
            Thing thing = pawn.inventory.innerContainer.FirstOrFallback(t => t.def.GetModExtension<ThingDefExtension_EnergySource>() != null && t.IngestibleNow);
            if (thing != null)
            {
                energySource = thing;
                return true;
            }

            // 否则，在地图上寻找
            energySource = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => t.def.GetModExtension<ThingDefExtension_EnergySource>() != null && t.IngestibleNow && !t.IsForbidden(pawn) && pawn.CanReserve(t)
            );

            return energySource != null;
        }
    }
}
