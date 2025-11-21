using System.Collections.Generic;
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
                    
                    // 使用自定义或默认的入口特效
                    if (Props.customEntryFleck != null)
                    {
                        // 自定义入口粒子效果
                        FleckMaker.Static(caster.Position, map, Props.customEntryFleck);
                    }
                    else
                    {
                        // 默认入口粒子效果
                        FleckMaker.Static(caster.Position, map, FleckDefOf.PsycastSkipFlashEntry);
                    }
                    
                    // 使用自定义或默认的出口特效
                    if (Props.customExitFleck != null)
                    {
                        // 自定义出口粒子效果
                        FleckMaker.Static(target.Cell, map, Props.customExitFleck);
                        // 如果需要更大的效果，可以创建多个粒子
                        if (Props.effectScale > 1.5f)
                        {
                            for (int i = 0; i < Mathf.FloorToInt(Props.effectScale); i++)
                            {
                                Vector3 offset = new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.5f, 0.5f));
                                FleckMaker.Static(target.Cell.ToVector3Shifted() + offset, map, Props.customExitFleck);
                            }
                        }
                    }
                    else
                    {
                        // 默认出口粒子效果
                        FleckMaker.Static(target.Cell, map, FleckDefOf.PsycastSkipInnerExit);
                        FleckMaker.Static(target.Cell, map, FleckDefOf.PsycastSkipOuterRingExit);
                    }
                    
                    // 播放传送音效
                    SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(caster.Position, map));
                    SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(target.Cell, map));
                },
                ticksAwayFromCast = 5
            };
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (!target.IsValid)
            {
                return;
            }

            Pawn caster = parent.pawn;
            Map map = caster.Map;

            // 使用自定义或默认的入口效果器
            EffecterDef entryEffecter = Props.customEntryEffecter ?? EffecterDefOf.Skip_Entry;
            Effecter entryEffect = entryEffecter.Spawn(caster, map);
            
            // 应用效果缩放
            if (Props.effectScale != 1.0f && entryEffect is Effecter effect)
            {
                // 这里可以添加效果缩放的逻辑
                // 注意：Effecter类可能没有直接的缩放属性，需要根据具体实现调整
            }
            
            parent.AddEffecterToMaintain(entryEffect, caster.Position, 60);
            
            // 使用自定义或默认的出口效果器
            EffecterDef exitEffecter = Props.customExitEffecter ?? EffecterDefOf.Skip_Exit;
            Effecter exitEffect = exitEffecter.Spawn(target.Cell, map);
            parent.AddEffecterToMaintain(exitEffect, target.Cell, 60);

            // 唤醒可能休眠的组件
            caster.TryGetComp<CompCanBeDormant>()?.WakeUp();
            
            // 执行传送
            caster.Position = target.Cell;
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
                // 根据效果缩放调整喧嚣半径
                float adjustedRadius = Props.destClamorRadius * Props.effectScale;
                GenClamor.DoClamor(caster, target.Cell, adjustedRadius, Props.destClamorType);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool showMessages = true)
        {
            // 检查目的地是否有效
            if (!CanTeleportTo(target.Cell, parent.pawn.Map))
            {
                if (showMessages)
                {
                    Messages.Message("CannotTeleportToLocation".Translate(), 
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
                return "CannotTeleportToLocation".Translate();
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
