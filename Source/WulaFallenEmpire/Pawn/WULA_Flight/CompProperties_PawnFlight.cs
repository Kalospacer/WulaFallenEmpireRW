using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public enum FlightCondition
    {
        Drafted,
        MechAlwaysExceptSpecialJobs  // 新增：机械族在非特殊工作状态下始终飞行
    }

    public class CompProperties_PawnFlight : CompProperties
    {
        // --- Custom Flight Logic ---
        public FlightCondition flightCondition = FlightCondition.Drafted;

        // --- 新增：机械族特殊工作检查 ---
        public List<JobDef> mechForbiddenJobs = new List<JobDef>
        {
            JobDefOf.MechCharge,    // 充电工作
            JobDefOf.SelfShutdown   // 关机工作
        };

        // --- Vanilla PawnKindDef Flight Parameters ---
        [NoTranslate]
        public string flyingAnimationFramePathPrefix;

        [NoTranslate]
        public string flyingAnimationFramePathPrefixFemale;

        public int flyingAnimationFrameCount;

        public int flyingAnimationTicksPerFrame = -1;

        public float flyingAnimationDrawSize = 1f;

        public bool flyingAnimationDrawSizeIsMultiplier;

        public bool flyingAnimationInheritColors;

        // --- Vanilla PawnKindLifeStage Flight Parameters ---
        public AnimationDef flyingAnimationEast;
        public AnimationDef flyingAnimationNorth;
        public AnimationDef flyingAnimationSouth;
        public AnimationDef flyingAnimationEastFemale;
        public AnimationDef flyingAnimationNorthFemale;
        public AnimationDef flyingAnimationSouthFemale;

        public CompProperties_PawnFlight()
        {
            compClass = typeof(CompPawnFlight);
        }
    }
}
