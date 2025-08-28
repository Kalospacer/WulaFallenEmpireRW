using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Verb_Excalibur : Verb
    {
        private new Pawn CasterPawn
        {
            get
            {
                return base.CasterPawn;
            }
        }

        private ThingWithComps weapon
        {
            get
            {
                return this.CasterPawn.equipment.Primary;
            }
        }

        private QualityCategory quality
        {
            get
            {
                return this.weapon.TryGetComp<CompQuality>().Quality;
            }
        }

        private float damageAmountBase
        {
            get
            {
                return this.weapon.def.tools.First<Tool>().power;
            }
        }

        private float armorPenetrationBase
        {
            get
            {
                return this.weapon.def.tools.First<Tool>().armorPenetration;
            }
        }

        private float damageAmount
        {
            get
            {
                if (this.ExcaliburProps.damageAmount > 0)
                {
                    return this.ExcaliburProps.damageAmount;
                }
                return 1.0f * this.damageAmountBase;
            }
        }

        private float armorPenetration
        {
            get
            {
                if (this.ExcaliburProps.armorPenetration >= 0)
                {
                    return this.ExcaliburProps.armorPenetration;
                }
                return 1.0f * this.armorPenetrationBase;
            }
        }

        private VerbProperties_Excalibur ExcaliburProps
        {
            get
            {
                return (VerbProperties_Excalibur)this.verbProps;
            }
        }

        protected override int ShotsPerBurst
        {
            get
            {
                return this.verbProps.burstShotCount;
            }
        }

        protected override bool TryCastShot()
        {
            // Calculate all affected cells once
            List<IntVec3> allAffectedCells = this.AffectedCells(this.currentTarget);
            
            // Create a beam for this specific burst
            Thing_ExcaliburBeam beam = (Thing_ExcaliburBeam)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("ExcaliburBeam", true), this.CasterPawn.Position, this.CasterPawn.Map);
            beam.caster = this.CasterPawn;
            beam.targetCell = this.currentTarget.Cell;
            beam.damageAmount = this.damageAmount;
            beam.armorPenetration = this.armorPenetration;
            beam.pathWidth = this.ExcaliburProps.pathWidth;
            beam.weaponDef = this.CasterPawn.equipment.Primary.def;
            beam.damageDef = this.ExcaliburProps.damageDef;
            beam.StartStrike(allAffectedCells, this.ShotsPerBurst, this.ShotsPerBurst);

            return true;
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            GenDraw.DrawFieldEdges(this.AffectedCells(target));
        }

        private List<IntVec3> AffectedCells(LocalTargetInfo target)
        {
            this.tmpCells.Clear();
            Vector3 vector = this.CasterPawn.Position.ToVector3Shifted().Yto0();
            IntVec3 endCell = this.TargetPosition(this.CasterPawn, target);
            this.tmpCells.Clear();
            foreach (IntVec3 cell in GenSight.BresenhamCellsBetween(this.CasterPawn.Position, endCell))
            {
                if (!cell.InBounds(this.CasterPawn.Map))
                {
                    break;
                }
                if (cell.GetEdifice(this.CasterPawn.Map) != null && cell.GetEdifice(this.CasterPawn.Map).def.passability == Traversability.Impassable)
                {
                    break;
                }
                // Add cells around the current cell based on pathWidth
                // Convert pathWidth to proper radius for GenRadial
                float radius = Math.Max(0.5f, this.ExcaliburProps.pathWidth - 0.5f);
                foreach (IntVec3 radialCell in GenRadial.RadialCellsAround(cell, radius, true))
                {
                    if (radialCell.InBounds(this.CasterPawn.Map) && !this.tmpCells.Contains(radialCell))
                    {
                        this.tmpCells.Add(radialCell);
                    }
                }
            }
            return this.tmpCells;
        }

        public IntVec3 TargetPosition(Pawn pawn, LocalTargetInfo currentTarget)
        {
            IntVec3 position = pawn.Position;
            IntVec3 cell = currentTarget.Cell;
            Vector3 direction = (cell - position).ToVector3().normalized;

            // Define a maximum range to prevent infinite loops or excessively long beams
            float maxRange = this.ExcaliburProps.maxRange; // Use configurable max range

            for (float i = 0; i < maxRange; i += 1f)
            {
                IntVec3 currentCell = (position.ToVector3() + direction * i).ToIntVec3();
                if (!currentCell.InBounds(pawn.Map))
                {
                    return currentCell; // Reached map boundary
                }
                // Check for walls or other impassable terrain
                if (currentCell.GetEdifice(pawn.Map) != null && currentCell.GetEdifice(pawn.Map).def.passability == Traversability.Impassable)
                {
                    return currentCell; // Hit an impassable wall
                }
            }
            return (position.ToVector3() + direction * maxRange).ToIntVec3(); // Reached max range
        }
        
        private bool CanUseCell(IntVec3 c)
        {
            return c.InBounds(this.CasterPawn.Map) && c != this.CasterPawn.Position;
        }

        private List<IntVec3> tmpCells = new List<IntVec3>();
    }
}