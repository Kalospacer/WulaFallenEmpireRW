using RimWorld;
using System.Linq;
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
            if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                Log.Message($"[JobGiver_WulaGetEnergy] {pawn.Name.ToStringShort} already has charging hediff. Priority 0.");
                return 0f;
            }
            
            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
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
            Log.Message($"[JobGiver_WulaGetEnergy] Trying to give job to {pawn.Name.ToStringShort}.");

            if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                Log.Message($"[JobGiver_WulaGetEnergy] {pawn.Name.ToStringShort} already has charging hediff. Job cancelled.");
                return null;
            }

            Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null || energyNeed.CurLevelPercentage >= maxEnergyLevelPercentage)
            {
                Log.Message($"[JobGiver_WulaGetEnergy] Energy level for {pawn.Name.ToStringShort} is sufficient. Job cancelled.");
                return null;
            }

            if (!TryFindBestEnergySourceFor(pawn, out Thing energySource))
            {
                Log.Message($"[JobGiver_WulaGetEnergy] No energy source found for {pawn.Name.ToStringShort}. Job cancelled.");
                return null;
            }

            if (energySource is Building_Bed)
            {
                Log.Message($"[JobGiver_WulaGetEnergy] Found bed for {pawn.Name.ToStringShort}. Creating WULA_LayDownToCharge job.");
                return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_LayDownToCharge"), energySource);
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy"), energySource);
            job.count = 1;
            return job;
        }

        private bool TryFindBestEnergySourceFor(Pawn pawn, out Thing energySource)
        {
            // 优先寻找可用的充电床
            energySource = FindChargingBed(pawn);
            if (energySource != null)
            {
                return true;
            }

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

        private Building_Bed FindChargingBed(Pawn pawn)
        {
            // 寻找附近可用的 WULA_Charging_Station_Synth
            Building_Bed bed = (Building_Bed)GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn),
                9999f,
                b =>
                {
                    Building_Bed bed_internal = b as Building_Bed;
                    if (bed_internal == null) return false;

                    var chargingComp = bed_internal.GetComp<CompChargingBed>();
                    if (chargingComp == null) return false;

                    var powerComp = bed_internal.GetComp<CompPowerTrader>();
                    return !bed_internal.IsForbidden(pawn) &&
                           pawn.CanReserve(bed_internal) &&
                           !bed_internal.Medical &&
                           !bed_internal.IsBurning() &&
                           powerComp != null &&
                           powerComp.PowerOn &&
                           !bed_internal.CurOccupants.Any();
                }
            );
            return bed;
        }
    }
}
