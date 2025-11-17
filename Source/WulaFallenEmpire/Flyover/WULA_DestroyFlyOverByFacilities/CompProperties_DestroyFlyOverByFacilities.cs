using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // CompProperties 定义
    public class CompProperties_DestroyFlyOverByFacilities : CompProperties_AbilityEffect
    {
        public CompProperties_DestroyFlyOverByFacilities()
        {
            compClass = typeof(CompAbilityEffect_DestroyFlyOverByFacilities);
        }
    }

    // Comp 实现
    public class CompAbilityEffect_DestroyFlyOverByFacilities : CompAbilityEffect
    {
        public new CompProperties_DestroyFlyOverByFacilities Props => (CompProperties_DestroyFlyOverByFacilities)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn?.Map == null) return;

            // 销毁所有 FlyOver 物体
            DestroyAllFlyOvers();
        }

        // 销毁所有 FlyOver
        private void DestroyAllFlyOvers()
        {
            List<Thing> flyOvers = new List<Thing>();
            
            // 获取地图上所有的 FlyOver
            foreach (Thing thing in parent.pawn.Map.listerThings.AllThings)
            {
                if (thing is FlyOver flyOver)
                {
                    flyOvers.Add(flyOver);
                }
            }
            
            // 销毁找到的 FlyOver
            foreach (FlyOver flyOver in flyOvers)
            {
                flyOver.EmergencyDestroy();
            }
            
            if (flyOvers.Count > 0)
            {
                Messages.Message($"WULA_DestroyFlyOver".Translate(), parent.pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}
