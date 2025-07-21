using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompProperties_WulaShieldBelt : CompProperties
    {
        public int maxShieldHitPoints = 200;
        public float shieldRadius = 3.0f;
        public bool interceptGroundProjectiles = true;
        public bool interceptAirProjectiles = false;
        public bool interceptMeleeAttacks = false;
        public bool empImmune = false;
        public Color shieldColor = new Color(0.2f, 0.6f, 1.0f);
        public float rechargeRate = 5.0f;
        public int rechargeCooldownTicks = 300;
        public SoundDef activeSound;
        public EffecterDef reactivateEffect;
        public bool startEnabled = false;
        
        // 护盾模式：true = 有生命值模式（可被破坏），false = 无生命值模式（类似低角护盾，只是偏转）
        public bool useHitPointsMode = true;

        public CompProperties_WulaShieldBelt()
        {
            compClass = typeof(CompWulaShieldBelt);
        }
    }
}