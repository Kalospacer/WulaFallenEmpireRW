// WorkGiver_RefuelMech.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_RefuelMech : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            // 返回所有需要燃料的机甲
            // 修复：使用 LINQ 的 Where 方法而不是 FindAll
            var mechs = pawn.Map.mapPawns.AllPawnsSpawned.Where(p => 
                p.TryGetComp<CompMechFuel>() != null);
            
            foreach (Pawn mech in mechs)
            {
                yield return mech;
            }
        }
        
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            // 如果没有需要燃料的机甲，跳过
            return !PotentialWorkThingsGlobal(pawn).Any();
        }
        
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn mech))
                return false;
            
            var fuelComp = mech.TryGetComp<CompMechFuel>();
            if (fuelComp == null)
                return false;
            
            // 检查机甲是否已加满燃料
            if (fuelComp.IsFull)
                return false;
            
            // 检查是否有可用的燃料
            if (FindFuel(pawn, fuelComp) == null)
                return false;
            
            // 检查是否能接触到机甲
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some))
                return false;
            
            // 检查机甲状态
            var pilotComp = mech.TryGetComp<CompMechPilotHolder>();
            bool hasPilot = pilotComp != null && pilotComp.HasPilots;
            
            // 如果有驾驶员且不是强制命令，不自动加注
            if (hasPilot && !forced)
                return false;
            
            // 检查燃料组件是否允许自动加注
            if (!fuelComp.Props.allowAutoRefuel && !forced)
                return false;
            
            // 检查是否达到自动加注阈值
            if (!forced && !fuelComp.NeedsRefueling)
                return false;
            
            return true;
        }
        
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var fuelComp = t.TryGetComp<CompMechFuel>();
            if (fuelComp == null)
                return null;
            
            // 寻找燃料
            Thing fuel = FindFuel(pawn, fuelComp);
            if (fuel == null)
                return null;
            
            // 创建加注工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_RefuelMech, t, fuel);
            job.count = fuelComp.GetFuelCountToFullyRefuel();
            return job;
        }
        
        // 修改方法：返回 Thing 而不是 bool
        private Thing FindFuel(Pawn pawn, CompMechFuel fuelComp)
        {
            if (fuelComp.FuelType == null)
                return null;
            
            // 在库存中寻找燃料
            Thing fuel = FindFuelInInventory(pawn, fuelComp.FuelType);
            if (fuel != null)
                return fuel;
            
            // 在地图上寻找燃料
            fuel = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(fuelComp.FuelType),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                validator: thing => !thing.IsForbidden(pawn) && pawn.CanReserve(thing)
            );
            
            return fuel;
        }
        
        private Thing FindFuelInInventory(Pawn pawn, ThingDef fuelType)
        {
            if (pawn.inventory == null)
                return null;
            
            return pawn.inventory.innerContainer.FirstOrDefault(t => t.def == fuelType);
        }
    }
}
