using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Verb_Wula_SustainedBeam : Verb
    {
        // --- Copied from Verb_ShootBeam for visual effects ---
        private MoteDualAttached mote;
        private Effecter endEffecter;
        private Sustainer sustainer;

        // --- Our custom state ---
        private int ticksLeft;
        private int ticksToNextDamage;
        private int explosionTicks;
        private Vector3 beamEnd;
        
        private VerbProperties_Wula_IonicBeam BeamProps => (VerbProperties_Wula_IonicBeam)verbProps;
        
        public override float? AimAngleOverride => (state == VerbState.Bursting) ? (beamEnd - caster.DrawPos).AngleFlat() : (float?)null;

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            
            var shotAngle = (currentTarget.Cell - caster.Position).AngleFlat;
            beamEnd = GetMapEdgePoint(caster.Position, shotAngle);

            if (verbProps.beamMoteDef != null)
            {
                mote = MoteMaker.MakeInteractionOverlay(verbProps.beamMoteDef, caster, new TargetInfo(beamEnd.ToIntVec3(), caster.Map));
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
                    mote.UpdateTargets(new TargetInfo(caster.Position, caster.Map), new TargetInfo(beamEnd.ToIntVec3(), caster.Map), Vector3.zero, Vector3.zero);
                    mote.Maintain();
                }
                if (endEffecter == null && verbProps.beamEndEffecterDef != null)
                {
                    endEffecter = verbProps.beamEndEffecterDef.Spawn(beamEnd.ToIntVec3(), caster.Map, Vector3.zero);
                }
                if (endEffecter != null)
                {
                    endEffecter.EffectTick(new TargetInfo(beamEnd.ToIntVec3(), caster.Map), TargetInfo.Invalid);
                }
                sustainer?.Maintain();

                // --- Beam Damage Logic ---
                ticksToNextDamage--;
                if (ticksToNextDamage <= 0)
                {
                    ApplyBeamDamage();
                    ticksToNextDamage = BeamProps.tickInterval;
                }
                
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
            this.ticksLeft = BeamProps.duration;
            this.ticksToNextDamage = 0; 
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

        private void ApplyBeamDamage()
        {
            var shotAngle = (beamEnd - caster.DrawPos).AngleFlat();
            var dinfo = new DamageInfo(verbProps.beamDamageDef ?? DamageDefOf.Burn, BeamProps.sustainedDamagePerTick, BeamProps.armorPenetration, shotAngle, caster, null, EquipmentSource?.def);
            var cellsInBeam = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEnd.ToIntVec3(), (int)verbProps.beamWidth);

            foreach (var cell in cellsInBeam)
            {
                if (!cell.InBounds(caster.Map)) continue;
                
                var thingsToHit = cell.GetThingList(caster.Map).Where(t => CanHit(t)).ToList();
                foreach (var thing in thingsToHit)
                {
                    thing.TakeDamage(dinfo);
                }
            }
        }
        
        private void ApplyPathExplosionDamage()
        {
            if (BeamProps.explosionDamageDef == null) return;

            var pathCells = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEnd.ToIntVec3(), (int)verbProps.beamWidth);
            var shotAngle = (beamEnd - caster.DrawPos).AngleFlat();
            var explosionDamageDef = BeamProps.explosionDamageDef;

            foreach (var cell in pathCells)
            {
                if (!cell.InBounds(caster.Map)) continue;
                
                if (cell.GetHashCode() % 3 != 0) continue; 

                var thingsToHit = cell.GetThingList(caster.Map).Where(t => CanHit(t)).ToList();
                foreach (var thing in thingsToHit)
                {
                    var dinfo = new DamageInfo(explosionDamageDef, explosionDamageDef.defaultDamage, explosionDamageDef.defaultArmorPenetration, shotAngle, caster, null, EquipmentSource?.def);
                    thing.TakeDamage(dinfo);
                }
                
                if(BeamProps.explosionCellFleck != null)
                {
                    FleckMaker.Static(cell, caster.Map, BeamProps.explosionCellFleck);
                }
                if (BeamProps.soundExplosion != null)
                {
                    BeamProps.soundExplosion.PlayOneShot(new TargetInfo(cell, caster.Map));
                }
                GenTemperature.PushHeat(cell, caster.Map, BeamProps.explosionHeatEnergyPerCell);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 0);
            Scribe_Values.Look(ref ticksToNextDamage, "ticksToNextDamage", 0);
            Scribe_Values.Look(ref explosionTicks, "explosionTicks");
            Scribe_Values.Look(ref beamEnd, "beamEnd");
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