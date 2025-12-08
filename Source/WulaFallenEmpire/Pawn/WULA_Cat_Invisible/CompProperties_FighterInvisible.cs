using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_FighterInvisible : CompProperties
    {
        public float BaseVisibleRadius = 14f;
        public int UndetectedTimeout = 120;
        public int CheckDetectedIntervalTicks = 7;
        public float FirstDetectedRadius = 30f;
        public int RevealedLetterDelayTicks = 6;
        public int AmbushCallMTBTicks = 600;
        
        // 修改：一个可定义的提供隐身的hediff
        public HediffDef InvisibilityDef;
        
        // 隐身冷却
        public int stealthCooldownTicks = 1200;
        
        // 新增：是否在视线内出现敌人时解除隐身
        public bool revealOnEnemyInSight = false;
        
        // 新增：解除隐身的检测半径（默认为FirstDetectedRadius）
        public float revealDetectionRadius = 500f;
        
        // 新增：是否显示解除隐身效果
        public bool showRevealEffect = true;
        
        // 新增：解除隐身时的声音
        public SoundDef revealSound;
        
        // 新增：是否发送解除隐身消息
        public bool sendRevealMessage = false;
        
        // 新增：解除隐身的最小敌人数量（默认为1）
        public int minEnemiesToReveal = 1;
        
        // 新增：敌人类型过滤器（空表示所有敌人）
        public List<ThingDef> enemyTypeFilter;
        
        // 新增：是否只在战斗状态时检查敌人
        public bool onlyCheckInCombat = false;
        
        // 新增：是否忽略某些状态的敌人（如倒地、死亡等）
        public bool ignoreDownedEnemies = true;
        public bool ignoreSleepingEnemies = false;
        
        public CompProperties_FighterInvisible()
        {
            compClass = typeof(CompFighterInvisible);
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            
            if (InvisibilityDef == null)
            {
                yield return "InvisibilityDef is not defined for CompProperties_FighterInvisible";
            }
            
            if (revealDetectionRadius <= 0)
            {
                revealDetectionRadius = FirstDetectedRadius;
            }
        }
    }
}
