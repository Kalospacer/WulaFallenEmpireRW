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

    public class HediffComp_MaintenanceDamage : HediffComp
    {
        private HediffCompProperties_MaintenanceDamage Props => (HediffCompProperties_MaintenanceDamage)props;

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);

            // 获取维护需求
            var maintenanceNeed = Pawn.needs?.TryGetNeed<Need_Maintenance>();
            if (maintenanceNeed == null)
                return;

            // 直接应用伤害惩罚
            maintenanceNeed.ApplyDamagePenalty(totalDamageDealt);
        }

        public override string CompTipStringExtra => "WULA_DamageAffectsMaintenance".Translate(Props.damageToMaintenanceFactor.ToStringPercent());
    }
}
