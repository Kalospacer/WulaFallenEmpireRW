using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class VerbProperties_WeaponStealBeam : VerbPropertiesExplosiveBeam
    {
        public HediffDef hediffToApply;
        public float hediffSeverityPerHit = 0.1f; // 每次命中增加的严重性百分比
        public float hediffMaxSeverity = 1.0f; // 达到此严重性时触发抢夺
        public bool removeHediffOnSteal = true; // 抢夺后是否移除hediff

        public VerbProperties_WeaponStealBeam()
        {
            verbClass = typeof(Verb_ShootWeaponStealBeam);
        }
    }
}