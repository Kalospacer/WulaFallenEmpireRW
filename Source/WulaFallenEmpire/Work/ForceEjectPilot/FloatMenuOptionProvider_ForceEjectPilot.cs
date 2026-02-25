// File: FloatMenuOptionProvider_ForceEjectPilot.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class FloatMenuOptionProvider_ForceEjectPilot : FloatMenuOptionProvider
    {
        // 征召状态下不能执行此工作
        protected override bool Drafted => true;
        
        // 非征召状态下可以执行
        protected override bool Undrafted => true;
        
        // 不支持多选
        protected override bool Multiselect => false;
        
        // 需要操纵能力
        protected override bool RequiresManipulation => true;
        
        // 检查Thing是否为机甲
        private bool IsMech(Pawn thing)
        {
            return thing is Wulamechunit || thing?.GetType()?.IsSubclassOf(typeof(Wulamechunit)) == true;
        }

        // 检查是否适用于当前上下文
        protected override bool AppliesInt(FloatMenuContext context)
        {
            // 必须有选中的殖民者
            if (context.FirstSelectedPawn == null)
                return false;
            
            // 检查点击的单元格中是否有机甲
            var ClickedPawns = context.ClickedPawns;
            if (ClickedPawns == null || ClickedPawns.Count == 0)
                return false;
            
            // 查找第一个机甲
            Pawn mech = null;
            foreach (var thing in ClickedPawns)
            {
                if (IsMech(thing))
                {
                    mech = thing;
                    break;
                }
            }
            
            if (mech == null)
                return false;
            
            // 检查机甲是否有驾驶员组件
            var comp = mech.TryGetComp<CompMechPilotHolder>();
            if (comp == null)
                return false;
            
            // 检查机甲是否属于非玩家派系，且Downed但未死亡，并且有驾驶员
            if (mech.Faction == Faction.OfPlayer || !mech.Downed || mech.Dead || !comp.HasPilots)
                return false;
            
            return true;
        }

        // 获取单个选项
        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            if (clickedPawn == null || context.FirstSelectedPawn == null)
                return null;
            
            // 如果不是机甲，返回null
            if (!IsMech(clickedPawn))
                return null;
            
            // 获取机甲和组件
            var mech = clickedPawn as Wulamechunit;
            var comp = mech?.TryGetComp<CompMechPilotHolder>();
            
            if (mech == null || comp == null)
                return null;
            
            // 检查机甲是否属于非玩家派系，且Downed但未死亡，并且有驾驶员
            if (mech.Faction == Faction.OfPlayer || !mech.Downed || mech.Dead || !comp.HasPilots)
                return null;
            
            // 检查殖民者是否能够执行此工作
            return CreateForceEjectOption(mech, context.FirstSelectedPawn, comp);
        }
        
        // 创建强制拉出驾驶员的菜单选项
        private FloatMenuOption CreateForceEjectOption(Wulamechunit mech, Pawn pawn, CompMechPilotHolder comp)
        {
            string label = "WULA_ForceEjectPilot".Translate(mech.LabelShort);
            string disabledReason = "";
            
            // 检查条件是否允许执行强制拉出
            bool canForceEject = CanForceEject(mech, pawn, comp, ref disabledReason);
            
            if (canForceEject)
            {
                return new FloatMenuOption(label, () =>
                {
                    // 创建强制拉出驾驶员的工作
                    Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_ForceEjectPilot, mech);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }, MenuOptionPriority.High);
            }
            else
            {
                // 创建禁用的选项，显示原因
                return new FloatMenuOption(
                    "WULA_ForceEjectPilot".Translate(mech.LabelShort) + ": " + disabledReason,
                    null,
                    MenuOptionPriority.DisabledOption);
            }
        }
        
        // 检查殖民者是否可以执行强制拉出
        private bool CanForceEject(Wulamechunit mech, Pawn pawn, CompMechPilotHolder comp, ref string disabledReason)
        {
            // 检查殖民者是否能够到达机甲
            if (!pawn.CanReach(mech, PathEndMode.Touch, Danger.Some))
            {
                disabledReason = "NoPath".Translate();
                return false;
            }
            
            // 检查殖民者状态
            if (pawn.Downed)
            {
                disabledReason = "Downed".Translate();
                return false;
            }
            
            if (pawn.Dead)
            {
                disabledReason = "Dead".Translate();
                return false;
            }
            
            // 检查是否为囚犯
            if (pawn.IsPrisoner)
            {
                disabledReason = "Prisoner".Translate();
                return false;
            }
            
            // 检查是否为奴隶
            if (pawn.IsSlave)
            {
                disabledReason = "Slave".Translate();
                return false;
            }
            
            // 检查机甲是否已经被玩家派系控制
            if (mech.Faction == Faction.OfPlayer)
            {
                disabledReason = "WULA_AlreadyPlayerMech".Translate();
                return false;
            }
            
            // 检查机甲是否Downed且未死亡
            if (!mech.Downed || mech.Dead)
            {
                disabledReason = "WULA_MechNotDowned".Translate();
                return false;
            }
            
            // 检查是否有驾驶员
            if (!comp.HasPilots)
            {
                disabledReason = "WULA_NoPilot".Translate();
                return false;
            }
            
            return true;
        }
    }
}
