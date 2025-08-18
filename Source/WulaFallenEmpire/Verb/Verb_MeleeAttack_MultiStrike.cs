using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class Verb_MeleeAttack_MultiStrike : Verb_MeleeAttack
    {
        private CompMultiStrike Comp
        {
            get
            {
                return this.EquipmentSource?.GetComp<CompMultiStrike>();
            }
        }

        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(LocalTargetInfo target)
        {
            DamageWorker.DamageResult result = new DamageWorker.DamageResult();
            if (this.Comp != null && target.HasThing)
            {
                int strikes = this.Comp.Props.strikeCount.RandomInRange;
                for (int i = 0; i < strikes; i++)
                {
                    if (target.ThingDestroyed)
                    {
                        break;
                    }
                    DamageInfo dinfo = new DamageInfo(this.verbProps.meleeDamageDef, this.verbProps.AdjustedMeleeDamageAmount(this, this.CasterPawn) * this.Comp.Props.damageMultiplierPerStrike, this.verbProps.AdjustedArmorPenetration(this, this.CasterPawn) * this.Comp.Props.damageMultiplierPerStrike, -1f, this.CasterPawn, null, this.EquipmentSource?.def);
                    dinfo.SetTool(this.tool);
                    DamageWorker.DamageResult damageResult = target.Thing.TakeDamage(dinfo);
                    result.totalDamageDealt += damageResult.totalDamageDealt;
                    result.wounded = (result.wounded || damageResult.wounded);
                    result.stunned = (result.stunned || damageResult.stunned);
                    if (damageResult.parts != null)
                    {
                        if (result.parts == null)
                        {
                            result.parts = new System.Collections.Generic.List<BodyPartRecord>();
                        }
                        result.parts.AddRange(damageResult.parts);
                    }
                }
            }
            else
            {
                DamageInfo dinfo2 = new DamageInfo(this.verbProps.meleeDamageDef, this.verbProps.AdjustedMeleeDamageAmount(this, this.CasterPawn), this.verbProps.AdjustedArmorPenetration(this, this.CasterPawn), -1f, this.CasterPawn, null, this.EquipmentSource?.def);
                dinfo2.SetTool(this.tool);
                result = target.Thing.TakeDamage(dinfo2);
            }
            return result;
        }
    }
}