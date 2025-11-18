using RimWorld;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    // HediffCompProperties 定义
    public class HediffCompProperties_DisappearWithEffect : HediffCompProperties_Disappears
    {
        public FleckDef fleckDef;                    // 要播放的特效
        public float fleckScale = 1f;               // 特效缩放
        public bool dropEquipment = false;          // 是否掉落装备（false = 直接销毁）
        public bool destroyCorpse = true;           // 是否销毁尸体
        public bool playSound = true;               // 是否播放音效
        public SoundDef soundDef;                   // 自定义音效

        public HediffCompProperties_DisappearWithEffect()
        {
            compClass = typeof(HediffComp_DisappearWithEffect);
        }
    }

    // HediffComp 实现
    public class HediffComp_DisappearWithEffect : HediffComp
    {
        public HediffCompProperties_DisappearWithEffect Props => 
            (HediffCompProperties_DisappearWithEffect)props;

        private int ticksUntilDisappear = -1;
        private bool triggered = false;

        public override void CompPostMake()
        {
            base.CompPostMake();
            if (Props.disappearsAfterTicks != null)
            {
                ticksUntilDisappear = Props.disappearsAfterTicks.RandomInRange;
            }
        }

        // 重写 CompPostTick 方法，在到达指定 tick 时触发效果
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            
            if (triggered || Pawn == null || Pawn.Destroyed) return;

            // 检查是否到达消失时间
            if (ticksUntilDisappear > 0)
            {
                ticksUntilDisappear--;
                if (ticksUntilDisappear <= 0)
                {
                    TriggerDisappearEffect();
                }
            }
            // 如果 pawn 已经死亡，立即触发效果
            else if (Pawn.Dead)
            {
                TriggerDisappearEffect();
            }
        }

        // 处理 pawn 死亡事件 - 修复参数问题
        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            if (!triggered)
            {
                TriggerDisappearEffect();
            }
        }

        // 触发消失效果的核心方法
        private void TriggerDisappearEffect()
        {
            if (triggered || Pawn == null || Pawn.Map == null || Pawn.Destroyed)
                return;

            triggered = true;

            try
            {
                // 记录位置
                IntVec3 position = Pawn.Position;
                Map map = Pawn.Map;

                // 1. 清除所有装备
                ClearAllEquipment(map);

                // 2. 播放特效
                PlayFleckEffect(position, map);

                // 3. 播放音效
                PlaySoundEffect(position, map);

                // 4. 发送消失消息（如果配置了）
                SendDisappearMessage();

                // 5. 删除 pawn
                DestroyPawn();

                Log.Message($"[DisappearWithEffect] Pawn {Pawn.LabelCap} disappeared at {position}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[DisappearWithEffect] Error in TriggerDisappearEffect: {ex}");
            }
        }

        // 清除所有装备 - 修复参数问题
        private void ClearAllEquipment(Map map)
        {
            if (Pawn.equipment == null && Pawn.apparel == null && Pawn.inventory == null)
                return;

            // 清除装备（武器）
            if (Pawn.equipment != null)
            {
                var allEquipment = Pawn.equipment.AllEquipmentListForReading.ListFullCopy();
                foreach (var thing in allEquipment)
                {
                    if (Props.dropEquipment)
                    {
                        // 掉落装备 - 修复类型转换问题
                        ThingWithComps droppedWeapon;
                        Pawn.equipment.TryDropEquipment(thing, out droppedWeapon, Pawn.Position, true);
                    }
                    else
                    {
                        // 直接销毁装备
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            // 清除 apparel（服装）
            if (Pawn.apparel != null)
            {
                var wornApparel = Pawn.apparel.WornApparel.ListFullCopy();
                foreach (var apparel in wornApparel)
                {
                    if (Props.dropEquipment)
                    {
                        // 掉落服装
                        Apparel droppedApparel;
                        Pawn.apparel.TryDrop(apparel, out droppedApparel, Pawn.Position, true);
                    }
                    else
                    {
                        // 直接销毁服装
                        apparel.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            // 清除 inventory（物品栏）
            if (Pawn.inventory != null)
            {
                var innerContainer = Pawn.inventory.innerContainer.InnerListForReading.ListFullCopy();
                foreach (var thing in innerContainer)
                {
                    if (Props.dropEquipment)
                    {
                        // 掉落物品
                        Thing droppedThing;
                        Pawn.inventory.innerContainer.TryDrop(thing, Pawn.Position, map, ThingPlaceMode.Near, out droppedThing);
                    }
                    else
                    {
                        // 直接销毁物品
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }

        // 播放特效
        private void PlayFleckEffect(IntVec3 position, Map map)
        {
            FleckDef fleckToUse = Props.fleckDef ?? FleckDefOf.PsycastAreaEffect;

            // 在 pawn 位置播放特效
            FleckMaker.Static(position, map, fleckToUse, Props.fleckScale);

            // 额外在周围格子上播放特效，增强视觉效果
            for (int i = 0; i < 8; i++)
            {
                IntVec3 nearbyCell = position + GenAdj.AdjacentCells[i];
                if (nearbyCell.InBounds(map))
                {
                    FleckMaker.Static(nearbyCell, map, fleckToUse, Props.fleckScale * 0.7f);
                }
            }
        }

        // 播放音效 - 修复音效播放问题
        private void PlaySoundEffect(IntVec3 position, Map map)
        {
            if (!Props.playSound) return;

            SoundDef soundToUse = Props.soundDef ?? SoundDefOf.PsycastPsychicPulse;
            
            // 使用 SoundStarter 播放音效
            SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(position, map));
            soundToUse.PlayOneShot(soundInfo);
        }

        // 发送消失消息
        private void SendDisappearMessage()
        {
            var disappearProps = props as HediffCompProperties_Disappears;
            if (disappearProps?.messageOnDisappear != null)
            {
                Messages.Message(disappearProps.messageOnDisappear.Translate(Pawn.LabelShort), 
                    Pawn, MessageTypeDefOf.NeutralEvent);
            }

            if (disappearProps?.letterLabelOnDisappear != null && 
                disappearProps?.letterTextOnDisappear != null && 
                Pawn.Faction == Faction.OfPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    disappearProps.letterLabelOnDisappear.Translate(Pawn.LabelShort),
                    disappearProps.letterTextOnDisappear.Translate(Pawn.LabelShort),
                    LetterDefOf.NeutralEvent,
                    new LookTargets(Pawn)
                );
            }
        }

        // 删除 pawn
        private void DestroyPawn()
        {
            if (Pawn.Dead && Props.destroyCorpse)
            {
                // 如果是尸体，直接销毁
                Pawn.Destroy();
            }
            else if (!Pawn.Dead)
            {
                // 如果是活着的 pawn，先杀死再销毁
                Pawn.Kill(null, this.parent);
                if (Props.destroyCorpse)
                {
                    Pawn.Destroy();
                }
            }
        }

        // 重写 CompTipStringExtra 显示额外信息
        public override string CompTipStringExtra
        {
            get
            {
                if (ticksUntilDisappear > 0 && Props.showRemainingTime)
                {
                    return "DisappearWithEffect_TimeRemaining".Translate(
                        ticksUntilDisappear.ToStringTicksToPeriod(Props.canUseDecimalsShortForm));
                }
                return null;
            }
        }

        // 重写 CompDebugString 用于调试
        public override string CompDebugString()
        {
            return $"Will disappear in: {ticksUntilDisappear} ticks (Triggered: {triggered})";
        }

        // 序列化数据
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilDisappear, "ticksUntilDisappear", -1);
            Scribe_Values.Look(ref triggered, "triggered", false);
        }
    }
}
