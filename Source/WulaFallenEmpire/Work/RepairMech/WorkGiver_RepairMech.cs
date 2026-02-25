// WorkGiver_RepairMech.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_RepairMech : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        
        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }
        
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer || pawn.Map == null)
                return Enumerable.Empty<Thing>();
            
            // 获取所有需要维修的玩家机甲
            return pawn.Map.mapPawns.AllPawnsSpawned
                .Where(p => 
                    p.Faction == Faction.OfPlayer && 
                    p.health != null &&
                    !p.Dead &&
                    p.TryGetComp<CompMechRepairable>()?.CanAutoRepair == true
                )
                .Cast<Thing>();
        }
        
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.story != null && pawn.WorkTagIsDisabled(WorkTags.Crafting))
                return true;
                
            return false;
        }
        
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn mech) || mech.Dead)
                return false;
                
            var repairableComp = t.TryGetComp<CompMechRepairable>();
            if (repairableComp == null || !repairableComp.CanAutoRepair)
                return false;
                
            if (!repairableComp.NeedsRepair)
                return false;
                
            if (pawn.Faction != Faction.OfPlayer)
                return false;
                
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, forced))
                return false;
                
            // 检查工作标签
            if (pawn.story != null && pawn.WorkTagIsDisabled(WorkTags.Crafting))
                return false;
                
            return true;
        }
        
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(Wula_JobDefOf.WULA_RepairMech, t);
        }
    }
}
