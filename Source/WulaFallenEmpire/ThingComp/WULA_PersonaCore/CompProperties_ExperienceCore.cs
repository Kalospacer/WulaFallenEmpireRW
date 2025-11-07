using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_ExperienceCore : CompProperties
    {
        // 经验阈值配置
        public List<ExperienceThreshold> experienceThresholds;
        
        // 技能类型：近战还是射击
        public SkillDef trackedSkill;
        
        // 经验获取倍率
        public float experienceMultiplier = 1.0f;
        
        // 是否显示经验信息
        public bool showExperienceInfo = true;
        
        // 数据包定义
        public ThingDef dataPackDef;
        
        // 弹出数据包时保留的经验比例 (0-1)
        public float experienceRetentionOnEject = 0f;
        
        // 吸收数据包时的搜索半径
        public int absorbRadius = 3;
        
        public CompProperties_ExperienceCore()
        {
            compClass = typeof(CompExperienceCore);
        }
    }

    public class ExperienceThreshold
    {
        public int experienceRequired;
        public QualityCategory quality;
        public string messageKey; // 升级消息的翻译键
    }
}
