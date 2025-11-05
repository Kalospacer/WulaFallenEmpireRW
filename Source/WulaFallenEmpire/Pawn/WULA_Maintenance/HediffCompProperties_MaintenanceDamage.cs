// HediffComp_MaintenanceDamage.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_MaintenanceDamage : HediffCompProperties
    {
        public float damageToMaintenanceFactor = 0.01f; // 每点伤害扣除的维护度比例

        public HediffCompProperties_MaintenanceDamage()
        {
            compClass = typeof(HediffComp_MaintenanceDamage);
        }
    }
    // 在 HediffComp_MaintenanceDamage 类中添加 StatDef 支持
    public class HediffComp_MaintenanceDamage : HediffComp
    {
        private HediffCompProperties_MaintenanceDamage Props => (HediffCompProperties_MaintenanceDamage)props;
        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
            var maintenanceNeed = Pawn.needs?.TryGetNeed<Need_Maintenance>();
            if (maintenanceNeed == null)
                return;
            // 使用 StatDef 值而不是硬编码值
            maintenanceNeed.ApplyDamagePenalty(totalDamageDealt);
        }
        public override string CompTipStringExtra
        {
            get
            {
                // 获取 StatDef 值
                var statDef = DefDatabase<StatDef>.GetNamedSilentFail("WULA_MaintenanceDamageToMaintenanceFactor");
                float damageFactor = statDef != null ? Pawn.GetStatValue(statDef) : Props.damageToMaintenanceFactor;

                return "WULA_DamageAffectsMaintenance".Translate(damageFactor.ToStringPercent());
            }
        }
    }
}
