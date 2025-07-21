using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_EmergencyEnergyRestore : CompAbilityEffect
    {
        public new CompProperties_AbilityEmergencyEnergyRestore Props => (CompProperties_AbilityEmergencyEnergyRestore)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            Pawn caster = parent.pawn;
            if (caster == null) return;

            // 检查是否是乌拉族
            if (!IsWulaRace(caster))
            {
                Messages.Message("只有乌拉族才能使用紧急能量恢复", MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 检查是否倒地（如果需要的话）
            if (Props.requireDowned && !caster.Downed)
            {
                Messages.Message("只能在倒地时使用紧急能量恢复", MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 添加Hediff
            if (Props.hediffDef != null)
            {
                var hediff = HediffMaker.MakeHediff(Props.hediffDef, caster);
                caster.health.AddHediff(hediff);
                
                Messages.Message($"{caster.LabelShort}激活了紧急能量恢复协议", MessageTypeDefOf.PositiveEvent, false);
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[EmergencyEnergyRestore] Applied to {caster.LabelShort}");
                }
            }
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            bool canApply = base.CanApplyOn(target, dest) && IsWulaRace(parent.pawn);
            
            if (Props.requireDowned)
            {
                canApply = canApply && parent.pawn.Downed;
            }
            
            return canApply;
        }

        private bool IsWulaRace(Pawn pawn)
        {
            if (pawn?.def == null) return false;
            return pawn.def.defName == "WulaSpecies";
        }
    }
}