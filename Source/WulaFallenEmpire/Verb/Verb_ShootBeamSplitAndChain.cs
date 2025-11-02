using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using WulaFallenEmpire.Utils;

namespace WulaFallenEmpire
{
    public class VerbProperties_SplitAndChain : VerbProperties
    {
        public bool isSplit = false;
        public int splitNum;
        public float splitRange;
        public int conductNum;
        public float conductRange;
        public float splitDamageFactor = 0.8f;
        public float conductDamageFactor = 0.6f;
        public float beamArmorPenetration = 0f;
        public int beamPathSteps = 15;
        public float flecksPerCell = 2f; // Flecks per cell to control beam density
        
        public FleckDef splitMoteDef;
        public FleckDef chainMoteDef;

        public VerbProperties_SplitAndChain()
        {
            this.verbClass = typeof(Verb_ShootBeamSplitAndChain);
        }
    }

    public class Verb_ShootBeamSplitAndChain : Verb
    {
        private VerbProperties_SplitAndChain Props => this.verbProps as VerbProperties_SplitAndChain;
        private Dictionary<Thing, List<Thing>> attackChains = new Dictionary<Thing, List<Thing>>();
        private Dictionary<Thing, Effecter> endEffecters = new Dictionary<Thing, Effecter>();
        private Sustainer sustainer;
        private int ticksToNextPathStep;

        public override void WarmupComplete()
        {
            this.Cleanup();

            List<Thing> mainTargets = new List<Thing>();
            if (!this.currentTarget.HasThing) { base.WarmupComplete(); return; }
            
            Thing primaryTarget = this.currentTarget.Thing;
            if (primaryTarget is Pawn p_primary && (p_primary.Dead || p_primary.Downed)) return;
            
            mainTargets.Add(primaryTarget);

            if (this.Props.isSplit && this.Props.splitNum > 0)
            {
                var potentialTargets = GenRadial.RadialDistinctThingsAround(primaryTarget.Position, this.caster.Map, this.Props.splitRange, false)
                    .OfType<Pawn>()
                    .Where(p => !p.Dead && !p.Downed && p.HostileTo(this.caster.Faction) && !mainTargets.Contains(p) && GenSight.LineOfSight(primaryTarget.Position, p.Position, this.caster.Map, true))
                    .OrderBy(p => p.Position.DistanceToSquared(primaryTarget.Position))
                    .Take(this.Props.splitNum);
                
                mainTargets.AddRange(potentialTargets);
            }

            foreach (Thing mainTarget in mainTargets)
            {
                List<Thing> currentChain = new List<Thing>();
                currentChain.Add(mainTarget);
                
                Thing lastTargetInChain = mainTarget;
                for (int i = 0; i < this.Props.conductNum; i++)
                {
                    Thing nextInChain = GenRadial.RadialDistinctThingsAround(lastTargetInChain.Position, this.caster.Map, this.Props.conductRange, false)
                        .OfType<Pawn>()
                        .Where(p => !p.Dead && !p.Downed && !currentChain.Contains(p) && !mainTargets.Except(new[]{mainTarget}).Contains(p) && this.Caster.HostileTo(p) && GenSight.LineOfSight(lastTargetInChain.Position, p.Position, this.caster.Map, true))
                        .OrderBy(p => p.Position.DistanceToSquared(lastTargetInChain.Position))
                        .FirstOrDefault();

                    if (nextInChain != null)
                    {
                        currentChain.Add(nextInChain);
                        lastTargetInChain = nextInChain;
                    }
                    else { break; }
                }
                attackChains[mainTarget] = currentChain;
            }

            this.burstShotsLeft = this.verbProps.burstShotCount;
            this.state = VerbState.Bursting;
            if (this.Props.soundCastBeam != null)
            {
                this.sustainer = this.Props.soundCastBeam.TrySpawnSustainer(SoundInfo.InMap(this.caster, MaintenanceType.PerTick));
            }
            base.TryCastNextBurstShot();
        }

        public override void BurstingTick()
        {
            if (this.burstShotsLeft <= 0)
            {
                this.Cleanup();
                base.BurstingTick();
                return;
            }
            
            List<Thing> deadOrInvalidChains = attackChains.Keys.Where(t => t == null || !t.Spawned).ToList();
            foreach (var key in deadOrInvalidChains)
            {
                if(endEffecters.ContainsKey(key))
                {
                    endEffecters[key].Cleanup();
                    endEffecters.Remove(key);
                }
                attackChains.Remove(key);
            }

            Vector3 casterPos = this.caster.DrawPos;
            foreach (var chainEntry in attackChains)
            {
                Thing mainTarget = chainEntry.Key;
                List<Thing> conductTargets = chainEntry.Value;

                DrawCurvedBeam(casterPos, mainTarget.DrawPos, Props.splitMoteDef ?? verbProps.beamLineFleckDef);

                for (int i = 0; i < conductTargets.Count - 1; i++)
                {
                    DrawCurvedBeam(conductTargets[i].DrawPos, conductTargets[i+1].DrawPos, Props.chainMoteDef ?? verbProps.beamLineFleckDef);
                }

                foreach (Thing target in conductTargets)
                {
                    if (!endEffecters.ContainsKey(target) || endEffecters[target] == null)
                    {
                        endEffecters[target] = verbProps.beamEndEffecterDef?.Spawn(target.Position, target.Map, Vector3.zero);
                    }
                    endEffecters[target]?.EffectTick(new TargetInfo(target), TargetInfo.Invalid);
                }
            }
            sustainer?.Maintain();
        }

        protected override bool TryCastShot()
        {
            if (this.attackChains.NullOrEmpty()) return false;

            bool anyDamaged = false;
            foreach (var chainEntry in attackChains)
            {
                Thing mainTarget = chainEntry.Key;
                List<Thing> conductTargets = chainEntry.Value;

                ApplyDamage(mainTarget, Props.splitDamageFactor);
                anyDamaged = true;

                for (int i = 1; i < conductTargets.Count; i++)
                {
                    ApplyDamage(conductTargets[i], Props.conductDamageFactor);
                }
            }
            
            this.ticksToNextPathStep = this.verbProps.ticksBetweenBurstShots;
            return anyDamaged;
        }

        private void DrawCurvedBeam(Vector3 start, Vector3 end, FleckDef fleckDef)
        {
            if (fleckDef == null) return;

            float magnitude = (end - start).MagnitudeHorizontal();
            if (magnitude <= 0) return;

            // 1. Generate Bezier curve points
            int segments = Mathf.Max(3, Mathf.CeilToInt(magnitude * Props.flecksPerCell));

            // --- ULTIMATE CURVE FIX ---
            // The control point must be offset perpendicular to the beam's direction on the XZ plane, not on the Y axis.
            Vector3 direction = (end - start).normalized;
            Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x); // Rotated 90 degrees on the XZ plane.

            Vector3 controlPoint = Vector3.Lerp(start, end, 0.5f) + perpendicular * magnitude * Props.beamCurvature;
            var path = BezierUtil.GenerateQuadraticPoints(start, controlPoint, end, segments);
            // 2. Check if there are enough points to connect
            if (path.Count < 2)
            {
                return;
            }

            // 3. Iterate through adjacent point pairs and draw connecting lines
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 pointA = path[i];
                Vector3 pointB = path[i + 1];
                FleckMaker.ConnectingLine(pointA, pointB, fleckDef, this.caster.Map, 1f);
            }
        }

        private void ApplyDamage(Thing thing, float damageFactor)
        {
            if (thing == null || verbProps.beamDamageDef == null) return;

            float totalDamage = verbProps.beamTotalDamage > 0 ? verbProps.beamTotalDamage / verbProps.burstShotCount : verbProps.beamDamageDef.defaultDamage;
            float finalDamage = totalDamage * damageFactor;

            var dinfo = new DamageInfo(verbProps.beamDamageDef, finalDamage, Props.beamArmorPenetration, -1, this.caster, null, base.EquipmentSource.def);
            thing.TakeDamage(dinfo);
        }

        private void Cleanup()
        {
            attackChains.Clear();
            foreach (var effecter in endEffecters.Values) effecter.Cleanup();
            endEffecters.Clear();
            sustainer?.End();
            sustainer = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}