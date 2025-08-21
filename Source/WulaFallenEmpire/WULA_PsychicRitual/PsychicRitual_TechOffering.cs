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

    public class PsychicRitual_TechOffering : PsychicRitualDef_Wula
    {
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
            var thingsInRadius = GenRadial.RadialDistinctThingsAround(center, map, offeringRadius, useCenter: true).ToList();

            // 创建一个可变的必需品计数器
            var requiredCounts = new Dictionary<ThingDef, int>();
            if (this.requiredOffering != null)
            {
                foreach (ThingDef thingDef in this.requiredOffering.filter.AllowedThingDefs)
                {
                    requiredCounts[thingDef] = (int)this.requiredOffering.GetBaseCount();
                }
            }

            float extraPowerFromOfferings = 0f;
            int offeringItemsCount = 0;

            if (!extraOfferings.NullOrEmpty())
            {
                var extraOfferingInfo = extraOfferings.ToDictionary(o => o.thingDef, o => o.power);

                // 遍历仪式范围内的所有物品
                foreach (Thing thing in thingsInRadius)
                {
                    // 检查这个物品是否可以作为额外祭品
                    if (extraOfferingInfo.TryGetValue(thing.def, out float powerPerItem))
                    {
                        int countInStack = thing.stackCount;

                        // 检查这个物品是否是必需品，并扣除相应数量
                        if (requiredCounts.TryGetValue(thing.def, out int requiredCount) && requiredCount > 0)
                        {
                            int numToFulfillRequirement = System.Math.Min(countInStack, requiredCount);
                            requiredCounts[thing.def] -= numToFulfillRequirement;
                            countInStack -= numToFulfillRequirement;
                        }

                        // 任何剩余的物品都算作额外祭品
                        if (countInStack > 0)
                        {
                            extraPowerFromOfferings += powerPerItem * countInStack;
                            offeringItemsCount += countInStack;
                        }
                    }
                }

                // 添加UI显示元素
                powerFactorsOut?.Add(new QualityFactor
                {
                    label = "WULA_ExtraOfferings".Translate(),
                    positive = offeringItemsCount > 0,
                    quality = extraPowerFromOfferings,
                    toolTip = "WULA_ExtraOfferings_Tooltip".Translate(),
                    count = offeringItemsCount > 0 ? "✓" : "✗" // 使用对勾/叉号来清晰显示状态
                });
            }

            power += extraPowerFromOfferings;
            power = UnityEngine.Mathf.Clamp01(power);
        }

        // 重写创建仪式步骤的方法
        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            // 获取基类的仪式步骤，这其中已经包含了等待 hoursUntilOutcome 的逻辑
            List<PsychicRitualToil> toils = base.CreateToils(psychicRitual, parent);
            
            // 在所有基类步骤之后，添加我们自定义的奖励步骤
            toils.Add(new PsychicRitualToil_TechOfferingOutcome(this));

            return toils;
        }
    }

    // 自定义的仪式步骤，用于处理奖励
    public class PsychicRitualToil_TechOfferingOutcome : PsychicRitualToil
    {
        private PsychicRitual_TechOffering ritualDef;

        // 需要一个无参构造函数用于序列化
        public PsychicRitualToil_TechOfferingOutcome() { }

        public PsychicRitualToil_TechOfferingOutcome(PsychicRitual_TechOffering def)
        {
            this.ritualDef = def;
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref ritualDef, "ritualDef");
        }

        public override void Start(PsychicRitual psychicRitual, PsychicRitualGraph graph)
        {
            float power = psychicRitual.PowerPercent;

            // 消耗祭品
            IntVec3 center = psychicRitual.assignments.Target.Cell;
            Map map = psychicRitual.assignments.Target.Map;
            float offeringRadius = 8f;

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
                Log.Error($"[WulaFallenEmpire] Reward weapon pool is empty for {ritualDef.defName}");
                return;
            }
            ThingDef weaponDef = ritualDef.rewardWeaponPool.RandomElement();
            if (weaponDef == null)
            {
                Log.Error($"[WulaFallenEmpire] Could not find weapon Def in reward pool for {ritualDef.defName}");
                return;
            }

            // 根据能量值决定物品品质
            QualityCategory quality = QualityCategory.Awful; // 默认最低品质
            if (!ritualDef.qualityThresholds.NullOrEmpty())
            {
                var sortedThresholds = ritualDef.qualityThresholds.OrderByDescending(t => t.threshold).ToList();
                foreach (var threshold in sortedThresholds)
                {
                    if (power >= threshold.threshold)
                    {
                        quality = threshold.quality;
                        break;
                    }
                }
            }
            else
            {
                if (power >= 1.0f) { quality = QualityCategory.Legendary; }
                else if (power >= 0.8f) { quality = QualityCategory.Masterwork; }
                else if (power >= 0.5f) { quality = QualityCategory.Excellent; }
                else if (power >= 0.2f) { quality = QualityCategory.Normal; }
                else { quality = QualityCategory.Poor; }
            }

            // 创建物品并设置品质
            Thing reward = ThingMaker.MakeThing(weaponDef);
            if (reward.TryGetComp<CompQuality>() is CompQuality compQuality)
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
        }
    }
}