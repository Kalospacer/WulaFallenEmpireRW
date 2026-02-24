// File: CompMechSkillInheritance_Simple.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 简化安全版：机甲技能继承自驾驶员
    /// </summary>
    public class CompMechSkillInheritance : ThingComp
    {
        private int ticksUntilUpdate = 0;
        private CompMechPilotHolder pilotHolder;
        private Pawn mechPawn;
        
        public CompProperties_MechSkillInheritance Props => 
            (CompProperties_MechSkillInheritance)props;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            mechPawn = parent as Pawn;
            pilotHolder = parent.TryGetComp<CompMechPilotHolder>();
            
            // 初始更新
            ticksUntilUpdate = Props.updateIntervalTicks;
            UpdateMechSkills();
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (mechPawn == null || mechPawn.skills == null)
                return;
            
            ticksUntilUpdate--;
            
            if (ticksUntilUpdate <= 0)
            {
                UpdateMechSkills();
                ticksUntilUpdate = Props.updateIntervalTicks;
            }
        }
        
        /// <summary>
        /// 更新机甲技能
        /// </summary>
        private void UpdateMechSkills()
        {
            if (mechPawn == null || mechPawn.skills == null)
                return;
            
            // 获取驾驶员组件
            var pilots = pilotHolder?.GetPilots()?.ToList() ?? new List<Pawn>();
            
            // 遍历机甲的所有技能
            foreach (var mechSkill in mechPawn.skills.skills)
            {
                if (mechSkill == null || mechSkill.TotallyDisabled)
                    continue;
                
                int maxLevel = Props.baseSkillLevelWhenNoPilot;
                
                // 如果有驾驶员，取最高等级
                if (pilots.Count > 0)
                {
                    foreach (var pilot in pilots)
                    {
                        if (pilot == null || pilot.skills == null)
                            continue;
                            
                        var pilotSkill = pilot.skills.GetSkill(mechSkill.def);
                        if (pilotSkill != null && !pilotSkill.TotallyDisabled)
                        {
                            int pilotLevel = pilotSkill.Level;
                            
                            // 应用倍率
                            int adjustedLevel = (int)(pilotLevel * Props.skillMultiplierForPilots);
                            
                            if (adjustedLevel > maxLevel)
                                maxLevel = adjustedLevel;
                        }
                    }
                }
                
                // 设置技能等级（使用Level属性，不要直接设置levelInt）
                mechSkill.Level = maxLevel;
                
                // 可选：重置经验值，防止自然变化
                if (Props.preventNaturalDecay)
                {
                    mechSkill.xpSinceLastLevel = 0f;
                    mechSkill.xpSinceMidnight = 0f;
                }
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilUpdate, "ticksUntilUpdate", 0);
        }
    }
    
    /// <summary>
    /// 简化版组件属性
    /// </summary>
    public class CompProperties_MechSkillInheritance : CompProperties
    {
        public int updateIntervalTicks = 250;
        public int baseSkillLevelWhenNoPilot = 0;
        public float skillMultiplierForPilots = 1.0f;
        public bool preventNaturalDecay = true; // 阻止技能自然遗忘
        
        public CompProperties_MechSkillInheritance()
        {
            compClass = typeof(CompMechSkillInheritance);
        }
    }
}
