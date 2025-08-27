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
        private Vector3 beamEnd;

        private VerbProperties_Wula_IonicBeam BeamProps => (VerbProperties_Wula_IonicBeam)verbProps;
        
        public override float? AimAngleOverride => (state == VerbState.Bursting) ? (beamEnd - caster.DrawPos).AngleFlat() : (float?)null;

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            
            // For sustained beam, it always reaches its max range
            var shotAngle = (currentTarget.Cell - caster.Position).AngleFlat;
            beamEnd = GetMapEdgePoint(caster.Position, shotAngle);

            // --- Copied Effect Logic ---
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
            // This verb is not a standard "burst", but we use the state to manage the effect
            if (ticksLeft > 0)
            {
                // --- Copied Effect Logic ---
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

                // --- Custom Damage Logic ---
                ticksLeft--;
                ticksToNextDamage--;
                if (ticksToNextDamage <= 0)
                {
                    ApplyDamage();
                    ticksToNextDamage = BeamProps.tickInterval;
                }

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
            this.ticksToNextDamage = 0; // First damage tick happens immediately

            return true;
        }

        private void ApplyDamage()
        {
            var shotAngle = (beamEnd - caster.DrawPos).AngleFlat();
            var dinfo = new DamageInfo(verbProps.beamDamageDef ?? DamageDefOf.Burn, BeamProps.sustainedDamagePerTick, BeamProps.armorPenetration, shotAngle, caster, EquipmentSource);
            var cellsInBeam = WulaBeamUtility.GetCellsInBeamArea(caster.Position, beamEnd.ToIntVec3(), verbProps.beamWidth);

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
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 0);
            Scribe_Values.Look(ref ticksToNextDamage, "ticksToNextDamage", 0);
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
            return start.toVector3() + direction * mapSize;
        }
    }
}