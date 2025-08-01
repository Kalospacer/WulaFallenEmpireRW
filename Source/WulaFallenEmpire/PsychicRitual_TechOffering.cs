using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    // 用于在XML中定义祭品
    public class OfferingItem
    {
        public ThingDef thingDef;
        public float power;
    }

    public class QualityThreshold
    {
        public float threshold;
        public QualityCategory quality;
    }

    public class PsychicRitual_TechOffering : PsychicRitualDef_InvocationCircle
    {
        // 仪式持续时间（小时）
        public new FloatRange hoursUntilOutcome;

        // 从XML加载的额外祭品列表
        public List<OfferingItem> extraOfferings = new List<OfferingItem>();

        // 从XML加载的奖励池
        public List<ThingDef> rewardWeaponPool = new List<ThingDef>();

        // 从XML加载的品质阈值
        public List<QualityThreshold> qualityThresholds = new List<QualityThreshold>();

        // 重写计算最大能量的方法
        public override void CalculateMaxPower(PsychicRitualRoleAssignments assignments, List<QualityFactor> powerFactorsOut, out float power)
        {
            // 首先调用基类方法
            base.CalculateMaxPower(assignments, powerFactorsOut, out power);

            IntVec3 center = assignments.Target.Cell;
            Map map = assignments.Target.Map;
            float offeringRadius = 8f;

            float extraPowerFromOfferings = 0f;
            if (!extraOfferings.NullOrEmpty())
            {
                var offeringThings = new Dictionary<ThingDef, float>();
                foreach(var offering in extraOfferings)
                {
                    offeringThings[offering.thingDef] = offering.power;
                }

                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, offeringRadius, useCenter: true))
                {
                    if (offeringThings.TryGetValue(thing.def, out float value))
                    {
                        extraPowerFromOfferings += value * thing.stackCount;
                    }
                }
            }

            if (extraPowerFromOfferings > 0)
            {
                powerFactorsOut?.Add(new QualityFactor
                {
                    label = "WULA_ExtraOfferings".Translate(),
                    positive = true,
                    quality = extraPowerFromOfferings,
                    toolTip = "WULA_ExtraOfferings_Tooltip".Translate()
                });
                power += extraPowerFromOfferings;
            }
            
            power = UnityEngine.Mathf.Clamp01(power);
        }

        // 重写创建仪式步骤的方法
        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            // 获取基类的仪式步骤
            List<PsychicRitualToil> toils = base.CreateToils(psychicRitual, parent);
            
            // 在最后添加我们自定义的奖励步骤
            toils.Add(new PsychicRitualToil_TechOfferingOutcome(psychicRitual, this));

            return toils;
        }
    }

    // 自定义的仪式步骤，用于处理奖励
    public class PsychicRitualToil_TechOfferingOutcome : PsychicRitualToil
    {
        private PsychicRitual psychicRitual;
        private PsychicRitualDef def;

        public PsychicRitualToil_TechOfferingOutcome(PsychicRitual psychicRitual, PsychicRitualDef def)
        {
            this.psychicRitual = psychicRitual;
            this.def = def;
        }

        public override bool Tick(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            float power = psychicRitual.power;

            // 消耗祭品
            IntVec3 center = psychicRitual.assignments.Target.Cell;
            Map map = psychicRitual.assignments.Target.Map;
            float offeringRadius = 8f;

            PsychicRitual_TechOffering ritualDef = (PsychicRitual_TechOffering)def;

            if (!ritualDef.extraOfferings.NullOrEmpty())
            {
                var offeringThings = new Dictionary<ThingDef, float>();
                foreach(var offering in ritualDef.extraOfferings)
                {
                    offeringThings[offering.thingDef] = offering.power;
                }

                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, offeringRadius, useCenter: true))
                {
                    if (offeringThings.ContainsKey(thing.def))
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            // 从奖励池中随机选择一个武器
            if (ritualDef.rewardWeaponPool.NullOrEmpty())
            {
                Log.Error($"[WulaFallenEmpire] Reward weapon pool is empty for {def.defName}");
                return true;
            }
            ThingDef weaponDef = ritualDef.rewardWeaponPool.RandomElement();
            if (weaponDef == null)
            {
                Log.Error($"[WulaFallenEmpire] Could not find weapon Def: {weaponDef.defName}");
                return true;
            }

            // 根据能量值决定物品品质
            QualityCategory quality = QualityCategory.Awful; // 默认最低品质
            if (!ritualDef.qualityThresholds.NullOrEmpty())
            {
                // 对阈值列表按阈值从高到低排序
                var sortedThresholds = ritualDef.qualityThresholds.OrderByDescending(t => t.threshold).ToList();
                foreach (var threshold in sortedThresholds)
                {
                    if (power >= threshold.threshold)
                    {
                        quality = threshold.quality;
                        break; // 找到第一个满足的阈值就跳出
                    }
                }
            }
            else // 如果XML中没有定义，则使用硬编码的默认值
            {
                if (power >= 1.0f) { quality = QualityCategory.Legendary; }
                else if (power >= 0.8f) { quality = QualityCategory.Masterwork; }
                else if (power >= 0.5f) { quality = QualityCategory.Excellent; }
                else if (power >= 0.2f) { quality = QualityCategory.Normal; }
                else { quality = QualityCategory.Poor; }
            }

            // 创建物品并设置品质
            Thing reward = ThingMaker.MakeThing(weaponDef);
            CompQuality compQuality = reward.TryGetComp<CompQuality>();
            if (compQuality != null)
            {
                compQuality.SetQuality(quality, ArtGenerationContext.Colony);
            }

            // 在仪式中心点生成奖励物品
            GenPlace.TryPlaceThing(reward, psychicRitual.assignments.Target.Cell, map, ThingPlaceMode.Near);

            // 发送消息通知玩家
            Find.LetterStack.ReceiveLetter(
                "WULA_RitualReward_Label".Translate(),
                "WULA_RitualReward_Description".Translate(reward.Label, quality.GetLabel()),
                LetterDefOf.PositiveEvent,
                new LookTargets(psychicRitual.assignments.Target.Cell, map)
            );
            
            return true;
        }
    }
}