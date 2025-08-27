using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Thing_ExcaliburBeam : Mote
    {
        public IntVec3 targetCell;
        public Pawn caster;
        public ThingDef weaponDef;
        public float damageAmount;
        public float armorPenetration;
        public float pathWidth;
        public DamageDef damageDef;
        
        // Burst shot support
        public int burstShotsTotal = 1;
        public int currentBurstShot = 0;
        
        // Path cells for this burst
        private List<IntVec3> currentBurstCells;

        private int ticksToDetonate = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Defs.Look(ref weaponDef, "weaponDef");
            Scribe_Values.Look(ref damageAmount, "damageAmount");
            Scribe_Values.Look(ref armorPenetration, "armorPenetration");
            Scribe_Values.Look(ref pathWidth, "pathWidth");
            Scribe_Defs.Look(ref damageDef, "damageDef");
            Scribe_Values.Look(ref burstShotsTotal, "burstShotsTotal", 1);
            Scribe_Values.Look(ref currentBurstShot, "currentBurstShot", 0);
        }

        public void StartStrike(List<IntVec3> allCells, int burstIndex, int totalBursts)
        {
            currentBurstCells = allCells;
            currentBurstShot = burstIndex;
            burstShotsTotal = totalBursts;
            ticksToDetonate = 1; // Start detonation immediately
        }

        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);
            if (ticksToDetonate > 0)
            {
                ticksToDetonate--;
                if (ticksToDetonate == 0)
                {
                    Detonate();
                }
            }
        }

        private void Detonate()
        {
            if (currentBurstCells == null || !currentBurstCells.Any())
            {
                Destroy();
                return;
            }

            // For this burst, we'll detonate all cells
            foreach (IntVec3 cell in currentBurstCells)
            {
                if (cell.InBounds(Map))
                {
                    // Apply explosion effect, but ignore the caster
                    List<Thing> ignoredThings = new List<Thing> { caster };
                    DamageDef explosionDamageType = damageDef ?? DamageDefOf.Bomb;
                    GenExplosion.DoExplosion(center: cell, map: Map, radius: 0.9f, damType: explosionDamageType, instigator: caster,
                                            damAmount: (int)damageAmount, armorPenetration: armorPenetration,
                                            explosionSound: null, weapon: weaponDef, projectile: null,
                                            intendedTarget: null, postExplosionSpawnThingDef: null,
                                            postExplosionSpawnChance: 0f, postExplosionSpawnThingCount: 1,
                                            postExplosionGasType: null, applyDamageToExplosionCellsNeighbors: false,
                                            preExplosionSpawnThingDef: null, preExplosionSpawnChance: 0f,
                                            preExplosionSpawnThingCount: 1, chanceToStartFire: 0f,
                                            damageFalloff: false, direction: null, ignoredThings: ignoredThings,
                                            affectedAngle: null, doVisualEffects: true, propagationSpeed: 0f,
                                            screenShakeFactor: 0f, doSoundEffects: true, postExplosionSpawnThingDefWater: null,
                                            flammabilityChanceCurve: null, overrideCells: null, postExplosionSpawnSingleThingDef: null, preExplosionSpawnSingleThingDef: null);
                }
            }
            Destroy();
        }
    }
}