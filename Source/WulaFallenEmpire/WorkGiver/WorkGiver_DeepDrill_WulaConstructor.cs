using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    // 扩展方法，用于检查特定种族
    public static class WorkGiverExtensions
    {
        // 检查是否为指定种族
        public static bool IsRace(this Pawn pawn, string raceDefName)
        {
            return pawn.def.defName == raceDefName;
        }
        
        // 检查是否为指定种族之一
        public static bool IsAnyRace(this Pawn pawn, params string[] raceDefNames)
        {
            string pawnRace = pawn.def.defName;
            foreach (string race in raceDefNames)
            {
                if (pawnRace == race)
                {
                    return true;
                }
            }
            return false;
        }
    }
    
    // 使用扩展方法的子类
    public class WorkGiver_DeepDrill_WulaCatConstructor : WorkGiver_DeepDrill
    {
        private const string AllowedRace = "Mech_WULA_Cat_Constructor";
        
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            
            // 检查种族
            if (!pawn.IsRace(AllowedRace))
            {
                return true;
            }
            
            // 调用基类方法
            bool shouldSkip = base.ShouldSkip(pawn, forced);
            
            return shouldSkip;
        }
        
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // 检查种族
            if (!pawn.IsRace(AllowedRace))
            {
                return false;
            }
            
            // 调用基类方法
            bool hasJob = base.HasJobOnThing(pawn, t, forced);
            
            return hasJob;
        }
        
        // 可选：自定义工作优先级
        public override float GetPriority(Pawn pawn, TargetInfo t)
        {
            if (!pawn.IsRace(AllowedRace))
            {
                return 0f; // 不是允许的种族，优先级为0
            }
            
            // 如果是允许的种族，提高优先级
            float basePriority = base.GetPriority(pawn, t);
            return basePriority * 1.5f; // 提高50%优先级
        }
        
        // 可选：提供自定义的工作描述
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!pawn.IsRace(AllowedRace))
            {
                return null;
            }
            
            
            Job job = base.JobOnThing(pawn, t, forced);
            if (job != null)
            {
                // 可以在这里添加自定义标签或其他修改
                job.playerForced = forced;
            }
            
            return job;
        }
    }
}
