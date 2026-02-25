// File: FloatMenuOptionProvider_EnterMech.cs
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class FloatMenuOptionProvider_EnterMech : FloatMenuOptionProvider
    {
        
        // 检查Thing是否为机甲
        private bool IsMech(Thing thing)
        {
            return thing is Wulamechunit || thing?.GetType()?.IsSubclassOf(typeof(Wulamechunit)) == true;
        }
        
        protected override bool Drafted => true; // 征召状态下不能进入机甲
        protected override bool Undrafted => true; // 非征召状态下可以进入
        protected override bool Multiselect => true; // 不支持多选
        
        // 检查是否适用于当前上下文
        protected override bool AppliesInt(FloatMenuContext context)
        {
            // 必须有选中的殖民者
            if (context.FirstSelectedPawn == null)
                return false;
            
            // 检查点击的单元格中是否有机甲
            var clickedThings = context.ClickedThings;
            if (clickedThings == null || clickedThings.Count == 0)
                return false;
            
            // 查找第一个机甲
            Thing mech = null;
            foreach (var thing in clickedThings)
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
            
            // 检查殖民者是否已经在机甲内
            // 由于CompMechPilotHolder没有ContainsPilot方法，我们需要通过其他方式检查
            if (IsPawnInMech(context.FirstSelectedPawn, mech))
                return false;
            
            return true;
        }
        
        // 检查殖民者是否已经在机甲内（替代ContainsPilot）
        private bool IsPawnInMech(Pawn pawn, Thing mech)
        {
            var comp = mech.TryGetComp<CompMechPilotHolder>();
            if (comp == null)
                return false;
            
            // 尝试通过内部容器检查
            var holder = comp as IThingHolder;
            if (holder != null)
            {
                var things = holder.GetDirectlyHeldThings();
                if (things != null && things.Contains(pawn))
                    return true;
            }
            
            // 或者尝试通过其他属性检查
            // 这里假设CompMechPilotHolder有HasPilots属性
            if (comp.HasPilots)
            {
                // 如果有必要，可以通过反射或其他方式检查具体驾驶员
                // 暂时返回false，假设不在机甲内
                return false;
            }
            
            return false;
        }
        
        // 获取单个选项
        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing == null || context.FirstSelectedPawn == null)
                return null;
            
            // 如果不是机甲，返回null
            if (!IsMech(clickedThing))
                return null;
            
            // 获取机甲和组件
            var mech = clickedThing as Wulamechunit;
            var comp = mech?.TryGetComp<CompMechPilotHolder>();
            
            if (mech == null || comp == null)
                return null;
            
            // 检查殖民者是否已经在机甲内
            if (IsPawnInMech(context.FirstSelectedPawn, mech))
                return null;
            
            // 检查各种条件，生成相应的菜单选项
            return CreateEnterMechOption(mech, context.FirstSelectedPawn, comp);
        }
        
        // 创建进入机甲的菜单选项
        private FloatMenuOption CreateEnterMechOption(Wulamechunit mech, Pawn pawn, CompMechPilotHolder comp)
        {
            string label = "WULA_EnterMech".Translate(mech.LabelShort);
            string disabledReason = "";
            
            // 检查条件是否允许进入
            bool canEnter = CanEnterMech(mech, pawn, comp, ref disabledReason);
            
            // 如果条件允许，创建可点击的选项
            if (canEnter)
            {
                return new FloatMenuOption(label, () =>
                {
                    // 创建进入机甲的工作
                    Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMech, mech);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    
                    // 播放音效（如果有的话）
                    FleckMaker.Static(mech.DrawPos, mech.MapHeld, FleckDefOf.FeedbackEquip);
                }, MenuOptionPriority.High);
            }
            else
            {
                // 创建禁用的选项，显示原因
                return new FloatMenuOption(
                    "WULA_EnterMech".Translate(mech.LabelShort) + ": " + disabledReason,
                    null,
                    MenuOptionPriority.DisabledOption);
            }
        }
        
        // 检查殖民者是否可以进入机甲
        private bool CanEnterMech(Wulamechunit mech, Pawn pawn, CompMechPilotHolder comp, ref string disabledReason)
        {
            // 检查机甲是否已满
            if (comp.IsFull)
            {
                disabledReason = "WULA_MechFull".Translate();
                return false;
            }
            
            // 检查殖民者是否可以成为驾驶员
            if (!comp.CanAddPilot(pawn))
            {
                disabledReason = "WULA_CannotBecomePilot".Translate();
                return false;
            }
            
            // 检查距离
            if (!pawn.CanReach(mech, PathEndMode.Touch, Danger.Deadly))
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
            
            // 检查机甲状态
            if (mech.Downed)
            {
                disabledReason = "Downed".Translate();
                return false;
            }
            
            if (mech.Dead)
            {
                disabledReason = "Dead".Translate();
                return false;
            }
            
            return true;
        }
    }
}
