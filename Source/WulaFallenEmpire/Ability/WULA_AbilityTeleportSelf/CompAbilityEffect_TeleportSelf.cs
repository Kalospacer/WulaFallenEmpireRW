using System.Collections.Generic;
using System.Linq; // 添加这个 using 指令
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_TeleportSelf : CompAbilityEffect
    {
        public static string SkipUsedSignalTag = "CompAbilityEffect.SkipUsed";

        public new CompProperties_AbilityTeleportSelf Props => (CompProperties_AbilityTeleportSelf)props;

        public override IEnumerable<PreCastAction> GetPreCastActions()
        {
            yield return new PreCastAction
            {
                action = delegate(LocalTargetInfo target, LocalTargetInfo dest)
                {
                    Pawn caster = parent.pawn;
                    Map map = caster.Map;
                    
                    // 新增：在施法前验证并调整目标位置
                    LocalTargetInfo adjustedTarget = AdjustTargetForBuildings(target, caster, map);
                    
                    // 使用调整后的目标位置
                    IntVec3 finalTargetCell = adjustedTarget.Cell;
                    
                    // 使用自定义或默认的入口特效
                    if (Props.customEntryFleck != null)
                    {
                        FleckMaker.Static(caster.Position, map, Props.customEntryFleck);
                    }
                    else
                    {
                        FleckMaker.Static(caster.Position, map, FleckDefOf.PsycastSkipFlashEntry);
                    }
                    
                    // 使用自定义或默认的出口特效
                    if (Props.customExitFleck != null)
                    {
                        FleckMaker.Static(finalTargetCell, map, Props.customExitFleck);
                        if (Props.effectScale > 1.5f)
                        {
                            for (int i = 0; i < Mathf.FloorToInt(Props.effectScale); i++)
                            {
                                Vector3 offset = new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.5f, 0.5f));
                                FleckMaker.Static(finalTargetCell.ToVector3Shifted() + offset, map, Props.customExitFleck);
                            }
                        }
                    }
                    else
                    {
                        FleckMaker.Static(finalTargetCell, map, FleckDefOf.PsycastSkipInnerExit);
                        FleckMaker.Static(finalTargetCell, map, FleckDefOf.PsycastSkipOuterRingExit);
                    }
                    
                    // 播放传送音效
                    SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(caster.Position, map));
                    SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(finalTargetCell, map));
                    
                    // 存储调整后的目标位置，供Apply方法使用
                    // 注意：这里使用反射或其他方法来设置目标，因为parent.ability可能不可直接访问
                    StoreAdjustedTarget(finalTargetCell);
                },
                ticksAwayFromCast = 5
            };
        }

        // 新增：存储调整后的目标位置
        private void StoreAdjustedTarget(IntVec3 targetCell)
        {
            // 这里可以使用一个字段来存储调整后的目标
            // 由于RimWorld的Ability类结构，我们可能需要使用其他方式
            // 暂时使用一个字段来存储
            this.adjustedTargetCell = targetCell;
        }

        private IntVec3 adjustedTargetCell = IntVec3.Invalid;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            Pawn caster = parent.pawn;
            Map map = caster.Map;

            // 新增：最终目标位置验证 - 优先使用调整后的目标
            IntVec3 finalTargetCell = this.adjustedTargetCell.IsValid ? this.adjustedTargetCell : target.Cell;
            finalTargetCell = ValidateAndAdjustTarget(finalTargetCell, caster, map);
            
            // 重置调整目标
            this.adjustedTargetCell = IntVec3.Invalid;

            // 如果无法找到有效位置，取消传送
            if (!CanTeleportTo(finalTargetCell, map))
            {
                Messages.Message("WULA_TeleportFailedNoValidLocation".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            // 使用自定义或默认的入口效果器
            EffecterDef entryEffecter = Props.customEntryEffecter ?? EffecterDefOf.Skip_Entry;
            Effecter entryEffect = entryEffecter.Spawn(caster, map);
            
            parent.AddEffecterToMaintain(entryEffect, caster.Position, 60);
            
            // 使用自定义或默认的出口效果器
            EffecterDef exitEffecter = Props.customExitEffecter ?? EffecterDefOf.Skip_Exit;
            Effecter exitEffect = exitEffecter.Spawn(finalTargetCell, map);
            parent.AddEffecterToMaintain(exitEffect, finalTargetCell, 60);

            // 唤醒可能休眠的组件
            caster.TryGetComp<CompCanBeDormant>()?.WakeUp();
            
            // 执行传送
            caster.Position = finalTargetCell;
            caster.Notify_Teleported();
            
            // 如果是玩家阵营，解除战争迷雾
            if ((caster.Faction == Faction.OfPlayer || caster.IsPlayerControlled) && caster.Position.Fogged(map))
            {
                FloodFillerFog.FloodUnfog(caster.Position, map);
            }
            
            // 传送后眩晕
            caster.stances.stunner.StunFor(Props.stunTicks.RandomInRange, caster, addBattleLog: false, showMote: false);
            
            // 发送传送信号
            SendSkipUsedSignal(caster.Position, caster);
            
            // 播放到达时的喧嚣效果
            if (Props.destClamorType != null)
            {
                float adjustedRadius = Props.destClamorRadius * Props.effectScale;
                GenClamor.DoClamor(caster, finalTargetCell, adjustedRadius, Props.destClamorType);
            }
            
            // 记录传送调整信息（调试用）
            if (finalTargetCell != target.Cell)
            {
                WulaLog.Debug($"[TeleportSelf] AI传送位置从 {target.Cell} 调整到 {finalTargetCell}");
            }
        }

        /// <summary>
        /// 新增：在施法前调整目标位置，防止传送到建筑上
        /// </summary>
        private LocalTargetInfo AdjustTargetForBuildings(LocalTargetInfo originalTarget, Pawn caster, Map map)
        {
            IntVec3 originalCell = originalTarget.Cell;
            
            // 如果目标位置不可传送，寻找最近的可行位置
            if (!CanTeleportTo(originalCell, map))
            {
                IntVec3 adjustedCell = FindNearestValidTeleportPosition(originalCell, caster, map);
                if (adjustedCell.IsValid)
                {
                    return new LocalTargetInfo(adjustedCell);
                }
            }
            
            return originalTarget;
        }

        /// <summary>
        /// 新增：最终目标位置验证
        /// </summary>
        private IntVec3 ValidateAndAdjustTarget(IntVec3 targetCell, Pawn caster, Map map)
        {
            // 如果目标位置不可传送，寻找替代位置
            if (!CanTeleportTo(targetCell, map))
            {
                IntVec3 adjustedCell = FindNearestValidTeleportPosition(targetCell, caster, map);
                if (adjustedCell.IsValid)
                {
                    return adjustedCell;
                }
            }
            
            return targetCell;
        }

        /// <summary>
        /// 新增：寻找最近的可行传送位置
        /// </summary>
        private IntVec3 FindNearestValidTeleportPosition(IntVec3 center, Pawn caster, Map map, int maxRadius = 15)
        {
            // 首先检查中心点本身
            if (CanTeleportTo(center, map))
                return center;

            // 在逐渐增大的半径内搜索
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                // 使用 GenRadial.RadialPattern 而不是 RadialCellsAround
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

            // 如果找不到有效位置，返回无效位置
            return IntVec3.Invalid;
        }

        public override bool Valid(LocalTargetInfo target, bool showMessages = true)
        {
            // 检查目的地是否有效
            if (!CanTeleportTo(target.Cell, parent.pawn.Map))
            {
                if (showMessages)
                {
                    Messages.Message("WULA_CannotTeleportToLocation".Translate(), 
                        new LookTargets(target.Cell, parent.pawn.Map), 
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
            // 检查是否在范围内
            if (Props.range > 0f && target.Cell.DistanceTo(parent.pawn.Position) > Props.range)
            {
                return false;
            }

            // 检查视线（如果需要）
            if (Props.requireLineOfSight && !GenSight.LineOfSight(parent.pawn.Position, target.Cell, parent.pawn.Map))
            {
                return false;
            }

            // 检查是否可以传送到该位置
            return CanTeleportTo(target.Cell, parent.pawn.Map);
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
                return "WULA_CannotTeleportToLocation".Translate();
            }
            return base.ExtraLabelMouseAttachment(target);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            // 绘制传送目的地的预览
            GenDraw.DrawTargetHighlight(target);
            
            // 绘制传送范围
            if (Props.range > 0)
            {
                GenDraw.DrawRadiusRing(parent.pawn.Position, Props.range);
            }
        }

        public static void SendSkipUsedSignal(LocalTargetInfo target, Thing initiator)
        {
            Find.SignalManager.SendSignal(new Signal(SkipUsedSignalTag, target.Named("POSITION"), initiator.Named("SUBJECT")));
        }
    }
}
