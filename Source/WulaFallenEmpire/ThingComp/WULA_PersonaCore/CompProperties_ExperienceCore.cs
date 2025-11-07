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
