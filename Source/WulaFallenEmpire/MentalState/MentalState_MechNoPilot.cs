using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class MentalState_MechNoPilot : MentalState
    {
        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            
            // 停止所有工作和移动
            pawn.jobs?.StopAll();
            pawn.pather?.StopDead();
            
            // 取消征召
            if (pawn.drafter != null && pawn.Drafted)
            {
                pawn.drafter.Drafted = false;
            }
            
            // 清除当前敌人目标
            pawn.mindState.enemyTarget = null;
        }
        
        public override void PostEnd()
        {
            base.PostEnd();
        }
        
        public override void MentalStateTick(int delta)
        {
            // 使用父类的tick逻辑，但不允许自动恢复
            if (pawn.IsHashIntervalTick(30, delta))
            {
                age += 30;
                
                // 只有在有驾驶员时才允许恢复
                // 检查会由 CompMechPilotHolder 处理
                // 这里不实现自动恢复逻辑
            }
        }
        
        // 重写以禁用敌对行为
        public override bool ForceHostileTo(Thing t)
        {
            return false;
        }
        
        public override bool ForceHostileTo(Faction f)
        {
            return false;
        }
        
        // 重写以禁用社交活动
        public override RandomSocialMode SocialModeMax()
        {
            return RandomSocialMode.Off;
        }
    }
}
