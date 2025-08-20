using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class ModExtension_Cone : DefModExtension
    {
        public float coneAngle = 10f;

        public float coneRange = 7f;

        public int repeatExplosionCount = 1;

        public ThingDef fragment;

        public int fragmentCount;

        public FloatRange? fragmentRange;

        public bool showConeEffect = true;

        public void DoConeExplosion(IntVec3 center, Map map, Quaternion rotation, DamageDef damType, Thing instigator, int damAmount = -1, float armorPenetration = -1f, SoundDef explosionSound = null, ThingDef weapon = null, ThingDef projectile = null, Thing intendedTarget = null, ThingDef postExplosionSpawnThingDef = null, float postExplosionSpawnChance = 0f, int postExplosionSpawnThingCount = 1, GasType? postExplosionGasType = null, float? postExplosionGasRadiusOverride = null, int postExplosionGasAmount = 255, bool applyDamageToExplosionCellsNeighbors = false, ThingDef preExplosionSpawnThingDef = null, float preExplosionSpawnChance = 0f, int preExplosionSpawnThingCount = 1, float chanceToStartFire = 0f, bool damageFalloff = false, float? direction = null, List<Thing> ignoredThings = null, float propagationSpeed = 1f, float excludeRadius = 0f, ThingDef postExplosionSpawnThingDefWater = null, float screenShakeFactor = 1f, SimpleCurve flammabilityChanceCurve = null, List<IntVec3> overrideCells = null)
        {
            Vector3 v = rotation * Vector3.forward;
            FloatRange initialAngleRange = new FloatRange(v.ToAngleFlat() - coneAngle, v.ToAngleFlat() + coneAngle);

            for (int i = 0; i < repeatExplosionCount; i++)
            {
                // Handle angle wrap-around for max > 360
                if (initialAngleRange.max > 360f)
                {
                    GenExplosion.DoExplosion(affectedAngle: new FloatRange(0f, initialAngleRange.max - 360f), center: center, map: map, radius: coneRange, damType: damType, instigator: instigator, damAmount: damAmount, armorPenetration: armorPenetration, explosionSound: explosionSound, weapon: weapon, projectile: projectile, intendedTarget: intendedTarget, postExplosionSpawnThingDef: postExplosionSpawnThingDef, postExplosionSpawnChance: postExplosionSpawnChance, postExplosionSpawnThingCount: postExplosionSpawnThingCount, postExplosionGasType: postExplosionGasType, postExplosionGasRadiusOverride: postExplosionGasRadiusOverride, postExplosionGasAmount: postExplosionGasAmount, applyDamageToExplosionCellsNeighbors: applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef: preExplosionSpawnThingDef, preExplosionSpawnChance: preExplosionSpawnChance, preExplosionSpawnThingCount: preExplosionSpawnThingCount, chanceToStartFire: chanceToStartFire, damageFalloff: damageFalloff, direction: direction, ignoredThings: ignoredThings, doVisualEffects: showConeEffect, propagationSpeed: propagationSpeed, excludeRadius: excludeRadius, doSoundEffects: showConeEffect, postExplosionSpawnThingDefWater: postExplosionSpawnThingDefWater, screenShakeFactor: screenShakeFactor, flammabilityChanceCurve: flammabilityChanceCurve, overrideCells: overrideCells);
                }

                // Handle angle wrap-around for min < 0
                if (initialAngleRange.min < 0f)
                {
                    GenExplosion.DoExplosion(affectedAngle: new FloatRange(initialAngleRange.min + 360f, 360f), center: center, map: map, radius: coneRange, damType: damType, instigator: instigator, damAmount: damAmount, armorPenetration: armorPenetration, explosionSound: explosionSound, weapon: weapon, projectile: projectile, intendedTarget: intendedTarget, postExplosionSpawnThingDef: postExplosionSpawnThingDef, postExplosionSpawnChance: postExplosionSpawnChance, postExplosionSpawnThingCount: postExplosionSpawnThingCount, postExplosionGasType: postExplosionGasType, postExplosionGasRadiusOverride: postExplosionGasRadiusOverride, postExplosionGasAmount: postExplosionGasAmount, applyDamageToExplosionCellsNeighbors: applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef: preExplosionSpawnThingDef, preExplosionSpawnChance: preExplosionSpawnChance, preExplosionSpawnThingCount: preExplosionSpawnThingCount, chanceToStartFire: chanceToStartFire, damageFalloff: damageFalloff, direction: direction, ignoredThings: ignoredThings, doVisualEffects: showConeEffect, propagationSpeed: propagationSpeed, excludeRadius: excludeRadius, doSoundEffects: showConeEffect, postExplosionSpawnThingDefWater: postExplosionSpawnThingDefWater, screenShakeFactor: screenShakeFactor, flammabilityChanceCurve: flammabilityChanceCurve, overrideCells: overrideCells);
                }
                
                // Main explosion
                GenExplosion.DoExplosion(center, map, coneRange, damType, instigator, damAmount, armorPenetration, explosionSound, weapon, projectile, intendedTarget, postExplosionSpawnThingDef, postExplosionSpawnChance, postExplosionSpawnThingCount, postExplosionGasType, postExplosionGasRadiusOverride, postExplosionGasAmount, applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef, preExplosionSpawnChance, preExplosionSpawnThingCount, chanceToStartFire, damageFalloff, direction, ignoredThings, initialAngleRange, showConeEffect, propagationSpeed, excludeRadius, showConeEffect, postExplosionSpawnThingDefWater, screenShakeFactor, flammabilityChanceCurve, overrideCells);
            }

            if (fragment != null)
            {
                FloatRange currentFragmentRange = fragmentRange.HasValue ? fragmentRange.Value : new FloatRange(0f, coneRange);
                IEnumerable<IntVec3> source = FragmentCells(center, initialAngleRange, currentFragmentRange);
                for (int j = 0; j < fragmentCount; j++)
                {
                    IntVec3 intVec = source.RandomElement();
                    ((Projectile)GenSpawn.Spawn(fragment, center, map)).Launch(instigator, intVec, intVec, ProjectileHitFlags.All);
                }
            }
        }

        private IEnumerable<IntVec3> FragmentCells(IntVec3 center, FloatRange? angle, FloatRange range)
        {
            int minRadialCells = GenRadial.NumCellsInRadius(range.min);
            int maxRadialCells = GenRadial.NumCellsInRadius(range.max);

            for (int i = minRadialCells; i < maxRadialCells; i++)
            {
                IntVec3 currentCell = center + GenRadial.RadialPattern[i];

                if (angle.HasValue)
                {
                    float angleMin = angle.Value.min;
                    float angleMax = angle.Value.max;
                    float lengthHorizontal = (currentCell - center).LengthHorizontal;

                    if (lengthHorizontal <= 0.5f) // Close to center, always include
                    {
                        yield return currentCell;
                        continue;
                    }

                    float cellAngle = Mathf.Atan2(-(currentCell.z - center.z), currentCell.x - center.x) * 57.29578f; // Convert radians to degrees

                    // Handle angle wrap-around for comparison
                    if (angleMin < 0f && cellAngle - angleMin > 360f)
                    {
                        cellAngle -= 360f;
                    }
                    if (angleMax > 360f && angleMax - cellAngle < 360f)
                    {
                        cellAngle += 360f;
                    }

                    // Check if cell is within the angular range
                    if (cellAngle >= angleMin && cellAngle <= angleMax)
                    {
                        yield return currentCell;
                    }
                }
                else
                {
                    yield return currentCell; // No angle restriction
                }
            }
        }
    }
}