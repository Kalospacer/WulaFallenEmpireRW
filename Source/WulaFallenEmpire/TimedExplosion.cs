using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace WulaFallenEmpire
{
    public class HediffComp_TimedExplosion : HediffComp
    {
        // 倒计时相关字段
        public int ticksToDisappear;
        public int disappearsAfterTicks;
        public int seed;

        // 配置属性快捷访问
        public HediffCompProperties_TimedExplosion Props =>
            (HediffCompProperties_TimedExplosion)props;

        // 消失判定属性
        public override bool CompShouldRemove
        {
            get
            {
                if (ticksToDisappear > 0) return false;
                if (Props.requiredMentalState != null)
                {
                    return parent.pawn.MentalStateDef != Props.requiredMentalState;
                }
                return true;
            }
        }

        // 进度计算
        public float Progress =>
            1f - (float)ticksToDisappear / Mathf.Max(1, disappearsAfterTicks);

        public int EffectiveTicksToDisappear => ticksToDisappear / TicksLostPerTick;

        public float NoisyProgress => AddNoiseToProgress(Progress, seed);

        public virtual int TicksLostPerTick => 1;

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (Props.showRemainingTime)
                {
                    if (EffectiveTicksToDisappear < 2500)
                    {
                        return EffectiveTicksToDisappear.ToStringSecondsFromTicks("F0");
                    }
                    return EffectiveTicksToDisappear.ToStringTicksToPeriod(allowSeconds: true, shortForm: true, canUseDecimals: true, allowYears: true, Props.canUseDecimalsShortForm);
                }
                return base.CompLabelInBracketsExtra;
            }
        }

        private static float AddNoiseToProgress(float progress, int seed)
        {
            float num = (float)Perlin.GetValue(progress, 0.0, 0.0, 9.0, seed);
            float num2 = 0.25f * (1f - progress);
            return Mathf.Clamp01(progress + num2 * num);
        }

        // 初始化
        public override void CompPostMake()
        {
            base.CompPostMake();
            disappearsAfterTicks = Props.disappearsAfterTicks.RandomInRange;
            seed = Rand.Int;
            ticksToDisappear = disappearsAfterTicks;
        }

        // 每帧更新
        public override void CompPostTick(ref float severityAdjustment)
        {
            ticksToDisappear--;
            if (CompShouldRemove)
            {
                parent.pawn.health.RemoveHediff(parent);
            }
        }

        // 移除后处理
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();

            // 处理新鲜伤口状态
            if (!Props.leaveFreshWounds && parent.Part != null)
            {
                foreach (BodyPartRecord part in parent.Part.GetPartAndAllChildParts())
                {
                    Hediff_MissingPart hediff = parent.pawn.health.hediffSet.GetMissingPartFor(part) as Hediff_MissingPart;
                    if (hediff != null)
                    {
                        hediff.IsFresh = false;
                    }
                }
            }

            // 触发爆炸逻辑
            if (ShouldTriggerExplosion())
            {
                TriggerExplosion();
                DestroyGearIfNeeded();
            }

            // 发送消息通知
            if (!Props.messageOnDisappear.NullOrEmpty() && PawnUtility.ShouldSendNotificationAbout(parent.pawn))
            {
                Messages.Message(
                    Props.messageOnDisappear.Formatted(parent.pawn.Named("PAWN")),
                    parent.pawn,
                    MessageTypeDefOf.NeutralEvent
                );
            }

            // 发送信件通知
            if (!Props.letterTextOnDisappear.NullOrEmpty() &&
            !Props.letterLabelOnDisappear.NullOrEmpty() &&
                PawnUtility.ShouldSendNotificationAbout(parent.pawn))
            {
                Find.LetterStack.ReceiveLetter(
                    Props.letterLabelOnDisappear.Formatted(parent.pawn.Named("PAWN")),
                    Props.letterTextOnDisappear.Formatted(parent.pawn.Named("PAWN")),
                    LetterDefOf.NegativeEvent,
                    parent.pawn
                );
            }
        }

        // 爆炸条件检查
        private bool ShouldTriggerExplosion()
        {
            return parent.pawn.Spawned &&
                   Props.explosionRadius > 0.01f &&
                   Props.damageDef != null &&
                   parent.pawn.Map != null;
        }

        // 执行爆炸
        private void TriggerExplosion()
        {
            GenExplosion.DoExplosion(
                center: parent.pawn.Position,
                map: parent.pawn.Map,
                radius: Props.explosionRadius,
                damType: Props.damageDef,
                instigator: parent.pawn,
                damAmount: Props.damageAmount,
                armorPenetration: Props.armorPenetration,
                explosionSound: Props.soundDef,
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: Props.postExplosionSpawnThingDef,
                postExplosionSpawnChance: Props.postExplosionSpawnChance,
                postExplosionSpawnThingCount: Props.postExplosionSpawnThingCount,
                postExplosionGasType: null,
                applyDamageToExplosionCellsNeighbors: false,
                chanceToStartFire: Props.chanceToStartFire,
                damageFalloff: Props.damageFalloff,
            direction: null,
                ignoredThings: new List<Thing> { parent.pawn }
            );
        }

        // 装备销毁
        private void DestroyGearIfNeeded()
        {
            if (!Props.destroyGear) return;

            if (parent.pawn.equipment != null)
            {
                parent.pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
            }
            if (parent.pawn.apparel != null)
            {
                parent.pawn.apparel.DestroyAll(DestroyMode.Vanish);
            }
        }

        // 数据持久化
        public override void CompExposeData()
        {
            Scribe_Values.Look(ref ticksToDisappear, "ticksToDisappear", 0);
            Scribe_Values.Look(ref disappearsAfterTicks, "disappearsAfterTicks", 0);
            Scribe_Values.Look(ref seed, "seed", 0);
        }

        // 调试信息
        public override string CompDebugString()
        {
            return $"倒计时: {ticksToDisappear}\n爆炸半径: {Props.explosionRadius}";
        }
    }

    public class HediffCompProperties_TimedExplosion : HediffCompProperties
    {
        [Header("消失设置")]
        public IntRange disappearsAfterTicks = new IntRange(600, 1200);
        public bool showRemainingTime = true;
        public bool canUseDecimalsShortForm;
        public MentalStateDef requiredMentalState;
        public bool leaveFreshWounds = true;

        [Header("爆炸设置")]
        public float explosionRadius = 3f;
        public DamageDef damageDef;
        public int damageAmount = 20;
        public float armorPenetration;
        public SoundDef soundDef;
        public float chanceToStartFire;
        public bool damageFalloff = true;

        [Header("后续效果")]
        public bool destroyGear;
        public GasType gasType;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance;
        public int postExplosionSpawnThingCount = 1;

        [Header("通知设置")]
        [MustTranslate]
        public string messageOnDisappear;
        [MustTranslate]
        public string letterLabelOnDisappear;
        [MustTranslate]
        public string letterTextOnDisappear;
        public bool sendLetterOnDisappearIfDead = true;

        public HediffCompProperties_TimedExplosion()
        {
            compClass = typeof(HediffComp_TimedExplosion);
        }
    }
}
