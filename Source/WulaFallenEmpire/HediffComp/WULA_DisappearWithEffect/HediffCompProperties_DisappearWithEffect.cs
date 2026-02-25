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

    // HediffComp 实现 - 只处理计时器到期
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

                // 1. 播放特效
                PlayFleckEffect(position, map);

                // 2. 播放音效
                PlaySoundEffect(position, map);

                // 3. 发送消失消息（如果配置了）
                SendDisappearMessage();

                // 4. 清除装备
                ClearAllEquipment();

                // 5. 暴力删除 pawn
                DestroyPawn();

                WulaLog.Debug($"[DisappearWithEffect] Pawn {Pawn.LabelCap} destroyed at {position}");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[DisappearWithEffect] Error in TriggerDisappearEffect: {ex}");
            }
        }

        // 清除所有装备
        private void ClearAllEquipment()
        {
            // 销毁装备（武器）
            if (Pawn.equipment != null)
            {
                if (Props.dropEquipment)
                {
                    // 掉落所有装备
                    var allEquipment = Pawn.equipment.AllEquipmentListForReading.ListFullCopy();
                    foreach (var thing in allEquipment)
                    {
                        ThingWithComps droppedWeapon;
                        Pawn.equipment.TryDropEquipment(thing, out droppedWeapon, Pawn.Position, true);
                    }
                }
                else
                {
                    // 直接销毁所有装备
                    Pawn.equipment.DestroyAllEquipment();
                }
            }

            // 销毁 apparel（服装）
            if (Pawn.apparel != null)
            {
                if (Props.dropEquipment)
                {
                    // 掉落所有服装
                    var wornApparel = Pawn.apparel.WornApparel.ListFullCopy();
                    foreach (var apparel in wornApparel)
                    {
                        Apparel droppedApparel;
                        Pawn.apparel.TryDrop(apparel, out droppedApparel, Pawn.Position, true);
                    }
                }
                else
                {
                    // 直接销毁所有服装
                    Pawn.apparel.DestroyAll();
                }
            }

            // 销毁 inventory（物品栏）
            if (Pawn.inventory != null && !Props.dropEquipment)
            {
                var innerContainer = Pawn.inventory.innerContainer.InnerListForReading.ListFullCopy();
                foreach (var thing in innerContainer)
                {
                    thing.Destroy(DestroyMode.Vanish);
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

        // 播放音效
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
            if (Pawn.Destroyed) return;

            try
            {
                // 直接调用 Destroy，绕过所有死亡逻辑
                Pawn.Destroy(DestroyMode.Vanish);
                
                WulaLog.Debug($"[DisappearWithEffect] Pawn {Pawn.LabelCap} destroyed");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[DisappearWithEffect] Error destroying pawn: {ex}");
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
