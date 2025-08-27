using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Verb_Wula_BreachingBeam : Verb
    {
        // --- Copied from Verb_ShootBeam for visual effects ---
        private MoteDualAttached mote;
        private Effecter endEffecter;
        private Sustainer sustainer;

        // --- Our custom state ---
        private Vector3 beamEndPoint;
        private int ticksLeft;
        private bool beamHitMapEdge;
        private int explosionTicks;
        private float beamEnergy; 
        
        private VerbProperties_Wula_IonicBeam BeamProps => (VerbProperties_Wula_IonicBeam)verbProps;
        
        public override float? AimAngleOverride => (state == VerbState.Bursting) ? (beamEndPoint - caster.DrawPos).AngleFlat() : (float?)null;

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            
            // --- Initial Damage and Path Calculation ---
            beamHitMapEdge = true; 
            float shotAngle = (currentTarget.Cell - caster.Position).AngleFlat;
            beamEndPoint = GetMapEdgePoint(caster.Position, shotAngle);
            var cellsOnPath = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEndPoint.ToIntVec3(), (int)verbProps.beamWidth);
            this.beamEnergy = BeamProps.breachingDamage;

            // This loop calculates the final beam end point based on the initial piercing damage
            foreach (var cell in cellsOnPath)
            {
                if (!cell.InBounds(caster.Map)) continue;
                var thingsToHit = cell.GetThingList(caster.Map).Where(t => CanHit(t)).ToList();
                
                foreach (var thing in thingsToHit)
                {
                    if (beamEnergy <= 0) break;
                    
                    float damageToDeal = Mathf.Min(beamEnergy, thing.HitPoints);
                    var dinfo = new DamageInfo(verbProps.beamDamageDef ?? DamageDefOf.Burn, damageToDeal, BeamProps.armorPenetration, shotAngle, caster, null, EquipmentSource?.def);
                    
                    thing.TakeDamage(dinfo);
                    beamEnergy -= thing.HitPoints;
                }

                if (beamEnergy <= 0)
                {
                    beamEndPoint = cell.ToVector3Shifted(); 
                    beamHitMapEdge = false;
                    break;
                }
            }
            
            // --- Start Visual Effects ---
            if (verbProps.beamMoteDef != null)
            {
                mote = MoteMaker.MakeInteractionOverlay(verbProps.beamMoteDef, caster, new TargetInfo(beamEndPoint.ToIntVec3(), caster.Map));
            }
            if (verbProps.soundCastBeam != null)
            {
                sustainer = verbProps.soundCastBeam.TrySpawnSustainer(SoundInfo.InMap(caster, MaintenanceType.PerTick));
            }
        }

        public override void BurstingTick()
        {
            if (ticksLeft > 0)
            {
                // --- Maintain Visual Effects ---
                if (mote != null)
                {
                    mote.UpdateTargets(new TargetInfo(caster.Position, caster.Map), new TargetInfo(beamEndPoint.ToIntVec3(), caster.Map), Vector3.zero, Vector3.zero);
                    mote.Maintain();
                }
                if (endEffecter == null && verbProps.beamEndEffecterDef != null)
                {
                    endEffecter = verbProps.beamEndEffecterDef.Spawn(beamEndPoint.ToIntVec3(), caster.Map, Vector3.zero);
                }
                if (endEffecter != null)
                {
                    endEffecter.EffectTick(new TargetInfo(beamEndPoint.ToIntVec3(), caster.Map), TargetInfo.Invalid);
                }
                sustainer?.Maintain();

                // --- Path Explosion Logic ---
                if (BeamProps.explosionEnabled)
                {
                    explosionTicks--;
                    if (explosionTicks <= 0)
                    {
                        ApplyPathExplosionDamage();
                        explosionTicks = BeamProps.explosionTickInterval;
                    }
                }

                ticksLeft--;
                if (ticksLeft <= 0)
                {
                    StopBeam();
                }
            }
        }

        protected override bool TryCastShot()
        {
            this.state = VerbState.Bursting;
            
            if (beamHitMapEdge)
            {
                this.ticksLeft = BeamProps.breachingBeamDuration;
            }
            else
            {
                this.ticksLeft = 1; 
            }
            
            this.explosionTicks = 0;

            return true;
        }

        private void StopBeam()
        {
            this.state = VerbState.Idle;
            mote?.Destroy();
            endEffecter?.Cleanup();
            sustainer?.End();
        }

        private void ApplyPathExplosionDamage()
        {
            if (this.beamEnergy <= 0 || BeamProps.explosionDamageDef == null) return;

            var pathCells = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEndPoint.ToIntVec3(), (int)verbProps.beamWidth);
            var shotAngle = (beamEndPoint - caster.DrawPos).AngleFlat();
            var explosionDamageDef = BeamProps.explosionDamageDef;

            foreach (var cell in pathCells)
            {
                if (this.beamEnergy <= 0) break;
                if (!cell.InBounds(caster.Map)) continue;
                
                // Performance optimization: don't create explosions on every single cell of the path
                if (cell.GetHashCode() % 2 != 0) continue;

                var thingsToHit = cell.GetThingList(caster.Map).Where(t => CanHit(t)).ToList();
                foreach (var thing in thingsToHit)
                {
                    if (this.beamEnergy <= 0) break;
                    
                    var dinfo = new DamageInfo(explosionDamageDef, explosionDamageDef.defaultDamage, explosionDamageDef.defaultArmorPenetration, shotAngle, caster, null, EquipmentSource?.def);
                    float damageDealt = Mathf.Min(thing.HitPoints, dinfo.Amount);
                    thing.TakeDamage(dinfo);
                    
                    this.beamEnergy -= damageDealt * BeamProps.explosionEnergyCostRatio;
                }
                
                if(explosionDamageDef?.explosionCellMote != null)
                {
                    FleckMaker.Static(cell, caster.Map, explosionDamageDef.explosionCellMote);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref beamEndPoint, "beamEndPoint");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref beamHitMapEdge, "beamHitMapEdge");
            Scribe_Values.Look(ref explosionTicks, "explosionTicks");
            Scribe_Values.Look(ref beamEnergy, "beamEnergy");
        }
        
        private bool CanHit(Thing t)
        {
            return t != null && t.Spawned && t != caster && !t.def.IsFilth;
        }

        private Vector3 GetMapEdgePoint(IntVec3 start, float angle)
        {
            float mapSize = Mathf.Max(caster.Map.Size.x, caster.Map.Size.z) * 1.5f;
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            return start.ToVector3() + direction * mapSize;
        }
    }
}