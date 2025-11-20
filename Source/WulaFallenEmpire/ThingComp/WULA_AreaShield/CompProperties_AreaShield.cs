using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompProperties_AreaShield : CompProperties
    {
        public float radius = 5.9f;
        public int baseHitPoints = 100;
        public int rechargeDelay = 3200;
        public int rechargeHitPointsIntervalTicks = 60;

        public EffecterDef absorbEffecter;
        public EffecterDef interceptEffecter;
        public EffecterDef breakEffecter;
        public EffecterDef reactivateEffecter;
        
        public Color color = Color.cyan;
        public int startupDelay = 0;

        // 拦截设置
        public bool interceptGroundProjectiles = true;
        public bool interceptNonHostileProjectiles = false;
        public bool interceptAirProjectiles = true;

        // 反射设置
        public bool canReflect = false;
        public float reflectChance = 0.5f;
        public float reflectAngleRange = 30f;
        public int reflectCost = 1;
        public EffecterDef reflectEffecter;

        public CompProperties_AreaShield()
        {
            compClass = typeof(ThingComp_AreaShield);
        }
    }
}
