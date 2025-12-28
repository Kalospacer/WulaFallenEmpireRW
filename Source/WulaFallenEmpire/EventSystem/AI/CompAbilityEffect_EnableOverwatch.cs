using System;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityEnableOverwatch : CompProperties_AbilityEffect
    {
        public int durationSeconds = 180; // Default 3 minutes

        public CompProperties_AbilityEnableOverwatch()
        {
            compClass = typeof(CompAbilityEffect_EnableOverwatch);
        }
    }

    public class CompAbilityEffect_EnableOverwatch : CompAbilityEffect
    {
        public new CompProperties_AbilityEnableOverwatch Props => (CompProperties_AbilityEnableOverwatch)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Map map = parent.pawn?.Map ?? Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("Error: No active map.", MessageTypeDefOf.RejectInput);
                return;
            }

            var overwatch = map.GetComponent<MapComponent_AIOverwatch>();
            if (overwatch == null)
            {
                overwatch = new MapComponent_AIOverwatch(map);
                map.components.Add(overwatch);
            }

            overwatch.EnableOverwatch(Props.durationSeconds);
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!base.CanApplyOn(target, dest))
                return false;

            Map map = parent.pawn?.Map ?? Find.CurrentMap;
            if (map == null)
                return false;

            var overwatch = map.GetComponent<MapComponent_AIOverwatch>();
            if (overwatch != null && overwatch.IsEnabled)
            {
                // Already active, show remaining time
                return false;
            }

            return true;
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            Map map = parent.pawn?.Map ?? Find.CurrentMap;
            if (map != null)
            {
                var overwatch = map.GetComponent<MapComponent_AIOverwatch>();
                if (overwatch != null && overwatch.IsEnabled)
                {
                    return $"Already active ({overwatch.DurationTicks / 60}s remaining)";
                }
            }
            return base.ExtraLabelMouseAttachment(target);
        }
    }
}
