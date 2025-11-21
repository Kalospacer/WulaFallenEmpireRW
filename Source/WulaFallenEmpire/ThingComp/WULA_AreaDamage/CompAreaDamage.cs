using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAreaDamage : ThingComp
    {
        private int ticksUntilNextDamage;
        
        public CompProperties_AreaDamage Props => (CompProperties_AreaDamage)props;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            ticksUntilNextDamage = Props.damageIntervalTicks;
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (!parent.Spawned)
                return;

            ticksUntilNextDamage--;
            if (ticksUntilNextDamage <= 0)
            {
                DoAreaDamage();
                ticksUntilNextDamage = Props.damageIntervalTicks;
            }
        }

        private void DoAreaDamage()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            // 获取范围内的所有物体
            List<Thing> thingsInRange = new List<Thing>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.radius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> thingList = cell.GetThingList(map);
                foreach (Thing thing in thingList)
                {
                    if (IsValidTarget(thing) && !thingsInRange.Contains(thing))
                    {
                        thingsInRange.Add(thing);
                    }
                }
            }

            // 对每个有效目标造成伤害
            foreach (Thing target in thingsInRange)
            {
                ApplyDamageToTarget(target);
            }
        }

        private bool IsValidTarget(Thing thing)
        {
            // 检查是否有生命值
            if (thing.def.useHitPoints == false || thing.HitPoints <= 0)
                return false;

            // 检查物体类型过滤
            if (thing is Building && !Props.affectBuildings)
                return false;
            if (thing is Pawn && !Props.affectPawns)
                return false;
            if (thing is Plant && !Props.affectPlants)
                return false;

            // 检查阵营关系（如果是生物）
            if (thing is Pawn pawn)
            {
                Faction targetFaction = pawn.Faction;
                Faction parentFaction = parent.Faction;
                
                if (targetFaction == null)
                {
                    if (!Props.affectNeutral)
                        return false;
                }
                else if (targetFaction == parentFaction)
                {
                    if (!Props.affectFriendly)
                        return false;
                }
                else if (targetFaction.HostileTo(parentFaction))
                {
                    if (!Props.affectHostile)
                        return false;
                }
                else
                {
                    if (!Props.affectNeutral)
                        return false;
                }
            }

            return true;
        }

        private void ApplyDamageToTarget(Thing target)
        {
            if (Props.damageDef == null)
                return;

            // 计算最终伤害量（应用缩放）
            int finalDamageAmount = CalculateFinalDamage(target);

            // 创建伤害信息
            DamageInfo damageInfo = new DamageInfo(
                Props.damageDef,
                finalDamageAmount,
                armorPenetration: 0f,
                instigator: parent,
                hitPart: null,
                weapon: null,
                category: DamageInfo.SourceCategory.ThingOrUnknown
            );

            // 应用伤害
            target.TakeDamage(damageInfo);
        }

        /// <summary>
        /// 计算最终伤害量，应用心灵敏感度缩放和保底伤害
        /// </summary>
        private int CalculateFinalDamage(Thing target)
        {
            float damageFactor = 1.0f;

            // 使用固定缩放值
            if (Props.useFixedScaling)
            {
                damageFactor = Props.fixedDamageFactor;
            }
            // 使用心灵敏感度缩放
            else if (Props.scaleWithPsychicSensitivity && target is Pawn pawn)
            {
                damageFactor = CalculatePsychicSensitivityFactor(pawn);
            }

            // 确保伤害倍率在最小和最大范围内
            damageFactor = Mathf.Clamp(damageFactor, Props.minDamageFactor, Props.maxDamageFactor);

            // 计算最终伤害
            int finalDamage = Mathf.RoundToInt(Props.damageAmount * damageFactor);
            
            // 确保至少造成1点伤害
            return Mathf.Max(1, finalDamage);
        }

        /// <summary>
        /// 根据目标的心灵敏感度计算伤害倍率
        /// </summary>
        private float CalculatePsychicSensitivityFactor(Pawn targetPawn)
        {
            // 获取心灵敏感度（如果目标没有心灵敏感度，使用默认值0.5）
            float psychicSensitivity = 0.5f;
            
            if (targetPawn.health != null && targetPawn.health.capacities != null)
            {
                psychicSensitivity = targetPawn.GetStatValue(StatDefOf.PsychicSensitivity);
            }

            // 返回心灵敏感度作为伤害倍率
            // 例如：敏感度0.5 = 0.5倍伤害，敏感度1.5 = 1.5倍伤害
            return psychicSensitivity;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextDamage, "ticksUntilNextDamage", Props.damageIntervalTicks);
        }

        /// <summary>
        /// 调试方法：显示伤害计算信息
        /// </summary>
        public string GetDamageDebugInfo(Thing target)
        {
            if (target is Pawn pawn)
            {
                float psychicSensitivity = pawn.GetStatValue(StatDefOf.PsychicSensitivity);
                float damageFactor = CalculatePsychicSensitivityFactor(pawn);
                int finalDamage = CalculateFinalDamage(target);
                
                return $"目标: {pawn.Label}\n" +
                       $"心灵敏感度: {psychicSensitivity:F2}\n" +
                       $"伤害倍率: {damageFactor:F2}\n" +
                       $"基础伤害: {Props.damageAmount}\n" +
                       $"最终伤害: {finalDamage}";
            }
            
            return $"目标: {target.Label}\n基础伤害: {Props.damageAmount}";
        }
    }
}
