using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_StunKnockback : CompProperties_AbilityEffect
    {
        // 伤害设置
        public DamageDef damageDef = DamageDefOf.Blunt;
        public float damageAmount = 15f;
        public float armorPenetration = 0f;
        
        // 眩晕设置
        public int stunTicks = 180; // 3秒眩晕
        
        // 击退设置
        public int maxKnockbackDistance = 5; // 最大击退距离
        public bool requireLineOfSight = true; // 击退路径是否需要视线
        public bool canKnockbackIntoWalls = false; // 是否可以击退到墙上
        
        // 飞行效果设置
        public ThingDef knockbackFlyerDef;
        public EffecterDef flightEffecterDef;
        public SoundDef landingSound;
        
        // 伤害和击退的视觉效果
        public EffecterDef impactEffecter;
        public SoundDef impactSound;

        public CompProperties_StunKnockback()
        {
            compClass = typeof(CompAbilityEffect_StunKnockback);
        }
    }
}
