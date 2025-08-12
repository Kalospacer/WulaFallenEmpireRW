using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Verb_MeleeAttack_Cleave : Verb_MeleeAttack
    {
        private CompCleave Comp
        {
            get
            {
                return this.EquipmentSource?.GetComp<CompCleave>();
            }
        }

        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(LocalTargetInfo target)
        {
            if (this.Comp == null)
            {
                // This verb should only be used with a weapon that has CompCleave
                return new DamageWorker.DamageResult();
            }

            DamageWorker.DamageResult result = new DamageWorker.DamageResult();

            // 1. 对主目标造成伤害
            DamageInfo dinfo = new DamageInfo(
                this.verbProps.meleeDamageDef,
                this.verbProps.AdjustedMeleeDamageAmount(this, this.CasterPawn),
                this.verbProps.AdjustedArmorPenetration(this, this.CasterPawn),
                -1f,
                this.CasterPawn,
                null,
                this.EquipmentSource?.def
            );
            dinfo.SetTool(this.tool);
            
            if (target.HasThing)
            {
                result = target.Thing.TakeDamage(dinfo);
            }

            // 2. 执行溅射伤害
            Pawn casterPawn = this.CasterPawn;
            if (casterPawn == null || !target.HasThing)
            {
                return result;
            }

            Thing mainTarget = target.Thing;
            Vector3 attackDirection = (mainTarget.Position - casterPawn.Position).ToVector3().normalized;
            bool mainTargetIsHostile = mainTarget.HostileTo(casterPawn);

            // 查找施法者周围的潜在目标
            IEnumerable<Thing> potentialTargets = GenRadial.RadialDistinctThingsAround(casterPawn.Position, casterPawn.Map, this.Comp.Props.cleaveRange, useCenter: true);

            foreach (Thing thing in potentialTargets)
            {
                // 跳过主目标、自己和非生物
                if (thing == mainTarget || thing == casterPawn || !(thing is Pawn secondaryTargetPawn))
                {
                    continue;
                }

                // 根据XML配置决定是否跳过倒地的生物
                if (!this.Comp.Props.damageDowned && secondaryTargetPawn.Downed)
                {
                    continue;
                }
                
                // 智能溅射：次要目标的敌对状态必须与主目标一致
                if (secondaryTargetPawn.HostileTo(casterPawn) != mainTargetIsHostile)
                {
                    continue;
                }

                // 检查目标是否在攻击扇形范围内
                Vector3 directionToTarget = (thing.Position - casterPawn.Position).ToVector3().normalized;
                float angle = Vector3.Angle(attackDirection, directionToTarget);

                if (angle <= this.Comp.Props.cleaveAngle / 2f)
                {
                    // 对次要目标造成伤害
                    DamageInfo cleaveDinfo = new DamageInfo(
                        this.verbProps.meleeDamageDef,
                        this.verbProps.AdjustedMeleeDamageAmount(this, casterPawn) * this.Comp.Props.cleaveDamageFactor,
                        this.verbProps.AdjustedArmorPenetration(this, casterPawn) * this.Comp.Props.cleaveDamageFactor,
                        -1f,
                        casterPawn,
                        null,
                        this.EquipmentSource?.def
                    );
                    cleaveDinfo.SetTool(this.tool);
                    secondaryTargetPawn.TakeDamage(cleaveDinfo);
                }
            }
            
            // 3. 创建扇形爆炸效果
            CreateCleaveExplosion(casterPawn, mainTarget, this.Comp.Props.cleaveRange, this.Comp.Props.cleaveAngle);

            return result;
        }

        private void CreateCleaveExplosion(Pawn caster, Thing target, float radius, float angle)
        {
            if (caster.Map == null || this.Comp.Props.explosionDamageDef == null) return;

            Vector3 direction = (target.Position - caster.Position).ToVector3().normalized;
            float baseAngle = direction.AngleFlat();
            
            float startAngle = baseAngle - (angle / 2f);
            float endAngle = baseAngle + (angle / 2f);

            GenExplosion.DoExplosion(
                center: caster.Position,
                map: caster.Map,
                radius: radius,
                damType: this.Comp.Props.explosionDamageDef,
                instigator: caster,
                damAmount: 0,
                armorPenetration: 0,
                explosionSound: null,
                weapon: this.EquipmentSource?.def,
                projectile: null,
                intendedTarget: target,
                postExplosionSpawnThingDef: null,
                postExplosionSpawnChance: 0f,
                postExplosionSpawnThingCount: 1,
                postExplosionGasType: null,
                applyDamageToExplosionCellsNeighbors: false,
                preExplosionSpawnThingDef: null,
                preExplosionSpawnChance: 0f,
                preExplosionSpawnThingCount: 1,
                chanceToStartFire: 0f,
                damageFalloff: false,
                direction: null, // Let affectedAngle handle the direction and arc
                ignoredThings: null,
                affectedAngle: new FloatRange(startAngle, endAngle),
                doVisualEffects: true,
                propagationSpeed: 1.7f,
                excludeRadius: 0.9f,
                doSoundEffects: false,
                screenShakeFactor: 0.2f
            );
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);

            if (target.IsValid && CasterPawn != null && this.Comp != null)
            {
                GenDraw.DrawFieldEdges(GetCleaveCells(target.Cell));
            }
        }

        private List<IntVec3> GetCleaveCells(IntVec3 center)
        {
            if (this.Comp == null)
            {
                return new List<IntVec3>();
            }

            IntVec3 casterPos = this.CasterPawn.Position;
            Map map = this.CasterPawn.Map;
            Vector3 attackDirection = (center - casterPos).ToVector3().normalized;

            return GenRadial.RadialCellsAround(casterPos, this.Comp.Props.cleaveRange, useCenter: true)
                .Where(cell => {
                    if (!cell.InBounds(map)) return false;
                    Vector3 directionToCell = (cell - casterPos).ToVector3();
                    if (directionToCell.sqrMagnitude <= 0.001f) return false; // Exclude caster's cell
                    return Vector3.Angle(attackDirection, directionToCell) <= this.Comp.Props.cleaveAngle / 2f;
                }).ToList();
        }
    }
}