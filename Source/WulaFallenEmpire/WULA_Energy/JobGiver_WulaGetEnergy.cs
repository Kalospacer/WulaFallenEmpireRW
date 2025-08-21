using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_WulaGetEnergy : ThinkNode_JobGiver
    {
        public float minEnergyLevelPercentage = 0.3f;
        public float maxEnergyLevelPercentage = 1.0f;

        public float emergencyPriority = 9.5f;

        public override float GetPriority(Pawn pawn)
        {
            var energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
            {
                return 0f;
            }

            // 如果能量已充满，则不需要充电
            if (energyNeed.CurLevel >= energyNeed.MaxLevel)
            {
                return 0f;
            }

            // 如果Pawn正在执行充电Job，并且能量尚未充满，则保持高优先级
            if ((pawn.CurJobDef == JobDefOf.LayDown ||
                 pawn.CurJobDef == DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy")) &&
                energyNeed.CurLevel < energyNeed.MaxLevel)
            {
                return emergencyPriority; // 保持高优先级，直到充满
            }
            
            // 如果能量低于阈值，则需要充电
            if (energyNeed.CurLevelPercentage < minEnergyLevelPercentage)
            {
                return emergencyPriority;
            }
            
            return 0f; // 否则，不需要充电，返回0
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            var energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
            {
                return null;
            }
            
            if (energyNeed.CurLevelPercentage >= maxEnergyLevelPercentage)
            {
                return null;
            }

            if (!TryFindBestEnergySourceFor(pawn, out var energySource))
            {
                return null;
            }

            if (energySource is Building_Bed)
            {
                return JobMaker.MakeJob(JobDefOf.LayDown, energySource);
            }

            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_IngestWulaEnergy"), energySource);
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

            // No bed found, now consider consumables.
            // Check for the hediff BEFORE searching for consumables.
            if (pawn.health.hediffSet.HasHediff(HediffDef.Named("WULA_ChargingHediff")))
            {
                energySource = null;
                return false;
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
                    if (!(b is Building_Bed bed_internal)) return false;

                    if (bed_internal.GetComp<CompChargingBed>() == null) return false;
                    
                    var powerComp = bed_internal.GetComp<CompPowerTrader>();

                    // A pawn can use a bed if:
                    // 1. It has power.
                    // 2. Its prisoner status matches the pawn's.
                    // 3. It's not a medical bed.
                    // 4. The pawn can reserve it (checks for ownership, forbidden, etc.)
                    return powerComp != null &&
                           powerComp.PowerOn &&
                           bed_internal.ForPrisoners == pawn.IsPrisoner &&
                           !bed_internal.Medical &&
                           pawn.CanReserve(bed_internal);
                }
            );
            return bed;
        }
    }
}
