using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using UnityEngine;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_StunKnockback : CompAbilityEffect
    {
        public new CompProperties_StunKnockback Props => (CompProperties_StunKnockback)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (target.HasThing && target.Thing is Pawn targetPawn)
            {
                // 第一步：造成伤害和眩晕
                bool targetDied = ApplyDamageAndStun(targetPawn);
                
                // 第二步：如果目标仍然存活，执行击退
                if (!targetDied && targetPawn != null && !targetPawn.Dead && !targetPawn.Downed)
                {
                    PerformKnockback(targetPawn);
                }
            }
        }

        /// <summary>
        /// 应用伤害和眩晕效果，返回目标是否死亡
        /// </summary>
        private bool ApplyDamageAndStun(Pawn targetPawn)
        {
            // 记录目标的初始状态（是否存活）
            bool wasAliveBeforeDamage = !targetPawn.Dead;

            // 创建伤害信息
            DamageInfo damageInfo = new DamageInfo(
                Props.damageDef,
                Props.damageAmount,
                Props.armorPenetration,
                -1f,
                parent.pawn,
                null
            );

            // 应用伤害
            targetPawn.TakeDamage(damageInfo);
            
            // 检查目标是否死亡
            bool targetDied = targetPawn.Dead || targetPawn.Destroyed || targetPawn == null;
            
            if (targetDied)
            {
                return true;
            }
            
            // 使用施法者的地图而不是目标的地图
            Map map = parent.pawn.Map;
            
            // 播放冲击效果
            if (Props.impactEffecter != null && map != null)
            {
                Effecter effect = Props.impactEffecter.Spawn();
                effect.Trigger(new TargetInfo(targetPawn.Position, map), new TargetInfo(targetPawn.Position, map));
                effect.Cleanup();
            }
            
            // 播放冲击音效
            if (Props.impactSound != null && map != null)
            {
                Props.impactSound.PlayOneShot(new TargetInfo(targetPawn.Position, map));
            }
            
            // 应用眩晕 - 只在目标存活时应用
            if (Props.stunTicks > 0 && !targetPawn.Dead)
            {
                targetPawn.stances.stunner.StunFor(Props.stunTicks, parent.pawn);
            }

            return false;
        }

        /// <summary>
        /// 执行击退
        /// </summary>
        private void PerformKnockback(Pawn targetPawn)
        {
            // 再次检查目标是否有效
            if (targetPawn == null || targetPawn.Destroyed || targetPawn.Dead)
            {
                return;
            }

            // 计算击退方向
            IntVec3 knockbackDirection = CalculateKnockbackDirection(targetPawn.Position);
            
            // 寻找最远的可站立击退位置
            IntVec3 knockbackDestination = FindFarthestStandablePosition(targetPawn, knockbackDirection);
            
            // 如果找到了有效位置，执行击退飞行
            if (knockbackDestination.IsValid && knockbackDestination != targetPawn.Position)
            {
                CreateKnockbackFlyer(targetPawn, knockbackDestination);
            }
        }

        /// <summary>
        /// 计算击退方向（施法者到目标的连线延长线）
        /// </summary>
        private IntVec3 CalculateKnockbackDirection(IntVec3 targetPosition)
        {
            // 从施法者指向目标的方向
            IntVec3 direction = targetPosition - parent.pawn.Position;
            
            // 标准化方向（保持整数坐标）
            if (direction.x != 0 || direction.z != 0)
            {
                // 找到主要方向分量
                if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                {
                    return new IntVec3(Mathf.Sign(direction.x) > 0 ? 1 : -1, 0, 0);
                }
                else
                {
                    return new IntVec3(0, 0, Mathf.Sign(direction.z) > 0 ? 1 : -1);
                }
            }
            
            // 如果施法者和目标在同一位置，使用随机方向
            return new IntVec3(Rand.Value > 0.5f ? 1 : -1, 0, 0);
        }

        /// <summary>
        /// 寻找最远的可站立击退位置（不检查路径通行性，只检查目标格子是否可站立）
        /// </summary>
        private IntVec3 FindFarthestStandablePosition(Pawn targetPawn, IntVec3 direction)
        {
            Map map = targetPawn.Map;
            IntVec3 currentPos = targetPawn.Position;
            IntVec3 farthestValidPos = currentPos;

            // 从最大距离开始向回找，找到第一个可站立的格子
            for (int distance = Props.maxKnockbackDistance; distance >= 1; distance--)
            {
                IntVec3 testPos = currentPos + (direction * distance);
                
                if (!testPos.InBounds(map))
                    continue;

                // 检查格子是否可站立且没有其他Pawn
                if (IsCellStandableAndEmpty(testPos, map, targetPawn))
                {
                    farthestValidPos = testPos;
                    break;
                }
            }

            return farthestValidPos;
        }

        /// <summary>
        /// 检查格子是否可站立且没有其他Pawn
        /// </summary>
        private bool IsCellStandableAndEmpty(IntVec3 cell, Map map, Pawn targetPawn)
        {
            if (!cell.InBounds(map))
                return false;

            // 检查是否可站立
            if (!cell.Standable(map))
                return false;

            // 检查是否有建筑阻挡（如果配置不允许击退到墙上）
            if (!Props.canKnockbackIntoWalls)
            {
                Building edifice = cell.GetEdifice(map);
                if (edifice != null && !(edifice is Building_Door))
                    return false;
            }

            // 检查视线（如果需要）
            if (Props.requireLineOfSight && !GenSight.LineOfSight(targetPawn.Position, cell, map))
                return false;

            // 检查是否有其他pawn
            List<Thing> thingList = cell.GetThingList(map);
            foreach (Thing thing in thingList)
            {
                if (thing is Pawn otherPawn && otherPawn != targetPawn)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 创建击退飞行器
        /// </summary>
        private void CreateKnockbackFlyer(Pawn targetPawn, IntVec3 destination)
        {
            Map map = targetPawn.Map;
            
            // 使用自定义飞行器或默认飞行器
            ThingDef flyerDef = Props.knockbackFlyerDef ?? ThingDefOf.PawnFlyer;
            
            // 创建飞行器（参考JumpUtility.DoJump）
            PawnFlyer flyer = PawnFlyer.MakeFlyer(
                flyerDef,
                targetPawn,
                destination,
                Props.flightEffecterDef,
                Props.landingSound,
                false, // 不携带物品
                null,  // 不覆盖起始位置
                null, // 传递Ability对象而不是CompAbilityEffect
                new LocalTargetInfo(destination)
            );

            if (flyer != null)
            {
                // 生成飞行器
                GenSpawn.Spawn(flyer, destination, map);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            // 首先调用基类验证
            if (!base.Valid(target, throwMessages))
                return false;

            // 检查目标是否为Pawn
            if (!target.HasThing || !(target.Thing is Pawn))
            {
                return false;
            }

            // 检查目标是否存活
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn.Dead || targetPawn.Downed)
            {
                return false;
            }

            // 检查是否在同一地图
            if (targetPawn.Map != parent.pawn.Map)
            {
                return false;
            }

            return true;
        }
    }
}
