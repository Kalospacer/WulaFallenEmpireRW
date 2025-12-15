using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_PullTarget : CompAbilityEffect
    {
        public static string PullUsedSignalTag = "CompAbilityEffect.PullUsed";

        public new CompProperties_AbilityPullTarget Props => (CompProperties_AbilityPullTarget)props;

        public override IEnumerable<PreCastAction> GetPreCastActions()
        {
            yield return new PreCastAction
            {
                action = delegate(LocalTargetInfo target, LocalTargetInfo dest)
                {
                    Pawn caster = parent.pawn;
                    Pawn targetPawn = target.Pawn;
                    Map map = caster.Map;
                    
                    if (targetPawn == null || !IsValidPullTarget(targetPawn))
                    {
                        Messages.Message("WULA_InvalidPullTarget".Translate(), caster, MessageTypeDefOf.RejectInput);
                        return;
                    }

                    // 在目标位置创建入口特效
                    if (Props.customEntryFleck != null)
                    {
                        FleckMaker.Static(targetPawn.Position, map, Props.customEntryFleck);
                    }
                    else
                    {
                        FleckMaker.Static(targetPawn.Position, map, FleckDefOf.PsycastSkipFlashEntry);
                    }
                    
                    // 播放拉取音效
                    SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(targetPawn.Position, map));
                    
                    // 计算拉取目的地
                    IntVec3 pullDestination = FindPullDestination(caster, targetPawn, map);
                    
                    // 存储拉取目的地
                    this.pullDestination = pullDestination;
                },
                ticksAwayFromCast = 5
            };
        }

        private IntVec3 pullDestination = IntVec3.Invalid;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            Pawn caster = parent.pawn;
            Pawn targetPawn = target.Pawn;
            Map map = caster.Map;

            if (targetPawn == null || !IsValidPullTarget(targetPawn))
            {
                Messages.Message("WULA_InvalidPullTarget".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            // 使用存储的拉取目的地或重新计算
            IntVec3 finalDestination = this.pullDestination.IsValid ? this.pullDestination : FindPullDestination(caster, targetPawn, map);
            this.pullDestination = IntVec3.Invalid;

            // 验证目的地
            finalDestination = ValidateAndAdjustDestination(finalDestination, targetPawn, map);
            
            if (!CanTeleportTo(finalDestination, map))
            {
                Messages.Message("WULA_PullFailedNoValidLocation".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            // 目标位置入口效果器
            EffecterDef entryEffecter = Props.customEntryEffecter ?? EffecterDefOf.Skip_Entry;
            Effecter entryEffect = entryEffecter.Spawn(targetPawn, map);
            parent.AddEffecterToMaintain(entryEffect, targetPawn.Position, 60);
            
            // 目的地出口效果器
            EffecterDef exitEffecter = Props.customExitEffecter ?? EffecterDefOf.Skip_Exit;
            Effecter exitEffect = exitEffecter.Spawn(finalDestination, map);
            parent.AddEffecterToMaintain(exitEffect, finalDestination, 60);

            // 执行传送（拉取）
            targetPawn.Position = finalDestination;
            targetPawn.Notify_Teleported();
            
            // 如果是玩家阵营，解除战争迷雾
            if ((targetPawn.Faction == Faction.OfPlayer || targetPawn.IsPlayerControlled) && targetPawn.Position.Fogged(map))
            {
                FloodFillerFog.FloodUnfog(targetPawn.Position, map);
            }
            
            // 拉取后眩晕目标
            targetPawn.stances.stunner.StunFor(Props.stunTicks.RandomInRange, caster, addBattleLog: true, showMote: true);
            
            // 发送拉取信号
            SendPullUsedSignal(targetPawn.Position, caster);
            
            // 播放到达时的喧嚣效果
            if (Props.destClamorType != null)
            {
                float adjustedRadius = Props.destClamorRadius * Props.effectScale;
                GenClamor.DoClamor(caster, finalDestination, adjustedRadius, Props.destClamorType);
            }
            
            // 播放拉取成功音效
            SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(finalDestination, map));
            
            // 记录拉取信息
            if (Prefs.DevMode)
            {
                WulaLog.Debug($"[PullTarget] {caster.Label} 将 {targetPawn.Label} 拉取到 {finalDestination}");
            }
        }

        /// <summary>
        /// 检查目标是否可以被拉取
        /// </summary>
        private bool IsValidPullTarget(Pawn target)
        {
            // 检查目标是否为Pawn
            if (target == null)
                return false;

            // 检查体型限制
            if (target.BodySize > Props.maxTargetBodySize)
            {
                Messages.Message("WULA_TargetTooLarge".Translate(target.LabelShort, Props.maxTargetBodySize), 
                    parent.pawn, MessageTypeDefOf.RejectInput);
                return false;
            }

            // 检查目标是否死亡或倒下
            if (target.Dead || target.Downed)
                return false;

            // 检查目标是否可被影响（根据派系关系）
            if (!Props.canPullHostile && target.HostileTo(parent.pawn.Faction))
                return false;

            if (!Props.canPullNeutral && target.Faction != null && !target.HostileTo(parent.pawn.Faction) && target.Faction != parent.pawn.Faction)
                return false;

            if (!Props.canPullFriendly && target.Faction == parent.pawn.Faction)
                return false;

            // 检查特殊免疫状态
            if (target.GetComp<CompImmuneToPull>() != null)
                return false;

            return true;
        }

        /// <summary>
        /// 寻找拉取目的地（施法者附近的合适位置）
        /// </summary>
        private IntVec3 FindPullDestination(Pawn caster, Pawn target, Map map)
        {
            // 在施法者周围寻找合适的拉取位置
            IntVec3 center = caster.Position;
            int searchRadius = Props.pullDestinationSearchRadius;
            
            // 优先选择施法者周围的空位
            for (int radius = 1; radius <= searchRadius; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
                {
                    if (cell.InBounds(map) && 
                        CanTeleportTo(cell, map) && 
                        cell != caster.Position && // 避免和目标重叠
                        cell != target.Position)
                    {
                        // 检查该位置是否在施法者视线内（可选）
                        if (!Props.requireLineOfSightToDestination || GenSight.LineOfSight(center, cell, map))
                        {
                            return cell;
                        }
                    }
                }
            }
            
            // 如果找不到合适位置，返回施法者位置（作为备选）
            return center;
        }

        /// <summary>
        /// 验证并调整拉取目的地
        /// </summary>
        private IntVec3 ValidateAndAdjustDestination(IntVec3 destination, Pawn target, Map map)
        {
            // 如果目的地不可传送，寻找替代位置
            if (!CanTeleportTo(destination, map))
            {
                IntVec3 adjustedCell = FindNearestValidTeleportPosition(destination, target, map, Props.maxPositionAdjustRadius);
                if (adjustedCell.IsValid)
                {
                    return adjustedCell;
                }
            }
            
            return destination;
        }

        /// <summary>
        /// 寻找最近的可行传送位置
        /// </summary>
        private IntVec3 FindNearestValidTeleportPosition(IntVec3 center, Pawn pawn, Map map, int maxRadius = 15)
        {
            // 首先检查中心点本身
            if (CanTeleportTo(center, map))
                return center;

            // 在逐渐增大的半径内搜索
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                int numCells = GenRadial.NumCellsInRadius(radius);
                for (int i = 0; i < numCells; i++)
                {
                    IntVec3 cell = center + GenRadial.RadialPattern[i];
                    if (cell.InBounds(map) && CanTeleportTo(cell, map))
                    {
                        return cell;
                    }
                }
            }

            return IntVec3.Invalid;
        }

        public override bool Valid(LocalTargetInfo target, bool showMessages = true)
        {
            Pawn targetPawn = target.Pawn;
            
            if (targetPawn == null)
            {
                if (showMessages)
                {
                    Messages.Message("WULA_MustTargetPawn".Translate(), 
                        new LookTargets(parent.pawn), 
                        MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            if (!IsValidPullTarget(targetPawn))
            {
                if (showMessages)
                {
                    // 错误消息已经在IsValidPullTarget中显示
                }
                return false;
            }

            // 检查最小距离 - 新增：不允许对身边一格的单位释放
            float distance = target.Cell.DistanceTo(parent.pawn.Position);
            if (distance <= Props.minRange)
            {
                if (showMessages)
                {
                    Messages.Message("WULA_TargetTooClose".Translate(Props.minRange), 
                        new LookTargets(parent.pawn), 
                        MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            // 检查最大距离
            if (distance > Props.range)
            {
                if (showMessages)
                {
                    Messages.Message("WULA_TargetOutOfRange".Translate(Props.range), 
                        new LookTargets(parent.pawn), 
                        MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            // 检查视线（如果需要）
            if (Props.requireLineOfSight && !GenSight.LineOfSight(parent.pawn.Position, target.Cell, parent.pawn.Map))
            {
                if (showMessages)
                {
                    Messages.Message("WULA_NoLineOfSight".Translate(), 
                        new LookTargets(parent.pawn), 
                        MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return base.Valid(target, showMessages);
        }

        /// <summary>
        /// 检查是否可以命中目标
        /// </summary>
        public bool CanHitTarget(LocalTargetInfo target)
        {
            Pawn targetPawn = target.Pawn;
            
            if (targetPawn == null)
                return false;

            // 检查距离
            float distance = target.Cell.DistanceTo(parent.pawn.Position);
            
            // 新增：检查最小距离
            if (distance <= Props.minRange)
                return false;

            if (Props.range > 0f && distance > Props.range)
                return false;

            // 检查视线（如果需要）
            if (Props.requireLineOfSight && !GenSight.LineOfSight(parent.pawn.Position, target.Cell, parent.pawn.Map))
                return false;

            // 检查目标有效性
            return IsValidPullTarget(targetPawn);
        }

        /// <summary>
        /// 检查是否可以传送到指定位置
        /// </summary>
        private bool CanTeleportTo(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            // 检查战争迷雾
            if (!Props.canTeleportToFogged && cell.Fogged(map))
                return false;

            // 检查屋顶
            if (!Props.canTeleportToRoofed && map.roofGrid.Roofed(cell))
                return false;

            // 检查是否可站立
            if (!cell.Standable(map))
                return false;

            // 检查是否有障碍物
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.surfaceType != SurfaceType.Item && 
                edifice.def.surfaceType != SurfaceType.Eat && !(edifice is Building_Door { Open: not false }))
            {
                return false;
            }

            // 检查是否有物品阻挡
            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def.category == ThingCategory.Item)
                {
                    return false;
                }
            }

            return true;
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            if (!CanHitTarget(target))
            {
                // 检查具体原因以提供更精确的错误信息
                Pawn targetPawn = target.Pawn;
                float distance = target.Cell.DistanceTo(parent.pawn.Position);
                
                if (targetPawn == null)
                    return "WULA_MustTargetPawn".Translate();
                
                if (distance <= Props.minRange)
                    return "WULA_TargetTooClose".Translate(Props.minRange);
                
                if (Props.range > 0f && distance > Props.range)
                    return "WULA_TargetOutOfRange".Translate(Props.range);
                
                if (Props.requireLineOfSight && !GenSight.LineOfSight(parent.pawn.Position, target.Cell, parent.pawn.Map))
                    return "WULA_NoLineOfSight".Translate();
                
                return "WULA_CannotPullTarget".Translate();
            }
            return base.ExtraLabelMouseAttachment(target);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            // 绘制目标高亮
            GenDraw.DrawTargetHighlight(target);
            
            // 绘制拉取范围
            if (Props.range > 0)
            {
                GenDraw.DrawRadiusRing(parent.pawn.Position, Props.range);
            }
            
            // 绘制最小范围环 - 新增：显示不允许释放的区域
            if (Props.minRange > 0)
            {
                GenDraw.DrawRadiusRing(parent.pawn.Position, Props.minRange, Color.red);
            }
            
            // 预览拉取目的地（如果可能）
            Pawn targetPawn = target.Pawn;
            if (targetPawn != null && IsValidPullTarget(targetPawn))
            {
                IntVec3 destination = FindPullDestination(parent.pawn, targetPawn, parent.pawn.Map);
                if (destination.IsValid)
                {
                    GenDraw.DrawCircleOutline(destination.ToVector3Shifted(), 1f, SimpleColor.Green);
                }
            }
        }

        public static void SendPullUsedSignal(LocalTargetInfo target, Thing initiator)
        {
            Find.SignalManager.SendSignal(new Signal(PullUsedSignalTag, target.Named("POSITION"), initiator.Named("SUBJECT")));
        }
    }

    // 可选的免疫组件
    public class CompImmuneToPull : ThingComp
    {
        // 简单的标记组件，表示该单位免疫拉取效果
    }
}
