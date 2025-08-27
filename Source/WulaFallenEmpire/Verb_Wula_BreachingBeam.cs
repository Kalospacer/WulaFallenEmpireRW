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
        private bool beamHitMapEdge; // NEW: Flag to check if the beam reached the map edge
        
        private VerbProperties_Wula_IonicBeam BeamProps => (VerbProperties_Wula_IonicBeam)verbProps;
        
        public override float? AimAngleOverride => (state == VerbState.Bursting) ? (beamEndPoint - caster.DrawPos).AngleFlat() : (float?)null;

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            
            // --- Custom Damage Logic ---
            beamHitMapEdge = true; // Assume it will hit the edge unless stopped
            float shotAngle = (currentTarget.Cell - caster.Position).AngleFlat;
            beamEndPoint = GetMapEdgePoint(caster.Position, shotAngle);
            var cellsOnPath = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEndPoint.ToIntVec3(), verbProps.beamWidth);
            var beamEnergy = BeamProps.breachingDamage; // Local variable for calculation

            // This loop calculates the final beam end point based on energy depletion
            foreach (var cell in cellsOnPath)
            {
                if (!cell.InBounds(caster.Map)) continue;
                var thingsToHit = cell.GetThingList(caster.Map).Where(t => CanHit(t)).ToList();
                
                foreach (var thing in thingsToHit)
                {
                    if (beamEnergy <= 0) break;
                    
                    float damageToDeal = Mathf.Min(beamEnergy, thing.HitPoints);
                    var dinfo = new DamageInfo(verbProps.beamDamageDef ?? DamageDefOf.Burn, damageToDeal, BeamProps.armorPenetration, shotAngle, caster, EquipmentSource);
                    
                    thing.TakeDamage(dinfo);
                    beamEnergy -= thing.HitPoints;
                }

                if (beamEnergy <= 0)
                {
                    beamEndPoint = cell.ToVector3Shifted(); // The beam stops here
                    beamHitMapEdge = false; // It was stopped, so it didn't hit the edge
                    break;
                }
            }
            
            // --- Copied Effect Logic ---
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
                // --- Copied Effect Logic ---
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

                ticksLeft--;
                if (ticksLeft <= 0)
                {
                    StopBeam();
                }
            }
        }

        protected override bool TryCastShot()
        {
            // The actual "shot" is just starting the effects, damage is pre-calculated in WarmupComplete
            this.state = VerbState.Bursting;
            
            // NEW: Set duration based on whether it hit the map edge
            if (beamHitMapEdge)
            {
                this.ticksLeft = BeamProps.breachingBeamDuration;
            }
            else
            {
                this.ticksLeft = 1; // Disappears almost instantly if blocked
            }

            return true;
        }

        private void StopBeam()
        {
            this.state = VerbState.Idle;
            mote?.Destroy();
            endEffecter?.Cleanup();
            sustainer?.End();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref beamEndPoint, "beamEndPoint");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref beamHitMapEdge, "beamHitMapEdge");
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