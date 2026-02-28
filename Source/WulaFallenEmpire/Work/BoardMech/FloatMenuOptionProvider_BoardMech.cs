using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class FloatMenuOptionProvider_BoardMech : FloatMenuOptionProvider
    {
        private static readonly List<Pawn> tmpPawns = new List<Pawn>();
        
        protected override bool Drafted => false; // 征召状态下不能登机
        protected override bool Undrafted => true; // 非征召状态下可以登机
        protected override bool Multiselect => true; // 支持多选
        
        // 检查Thing是否为机甲
        private bool IsMech(Thing thing)
        {
            // 检查是否有CompMechCrewHolder组件
            return thing?.TryGetComp<CompMechCrewHolder>() != null;
        }
        
        // 检查Pawn是否在机甲内
        private bool IsPawnInMech(Pawn pawn, Thing mech)
        {
            var comp = mech.TryGetComp<CompMechCrewHolder>();
            if (comp == null)
                return false;
                
            // 检查内部容器
            var holder = comp as IThingHolder;
            if (holder != null)
            {
                var things = holder.GetDirectlyHeldThings();
                if (things != null && things.Contains(pawn))
                    return true;
            }
            
            return false;
        }
        
        protected override bool AppliesInt(FloatMenuContext context)
        {
            // 必须有选中的殖民者
            if (context.FirstSelectedPawn == null)
                return false;
                
            // 检查点击的单元格中是否有机甲
            var clickedThings = context.ClickedThings;
            if (clickedThings == null || clickedThings.Count == 0)
                return false;
                
            // 查找第一个有机甲乘员组件的Thing
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
                
            return true;
        }
        
        // 重写：获取选项
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (!AppliesInt(context))
                yield break;
                
            if (context.IsMultiselect)
            {
                var option = GetMultiselectBoardMechOption(clickedThing, context);
                if (option != null)
                    yield return option;
                yield break;
            }
            
            var singleOption = GetSingleOptionFor(clickedThing, context);
            if (singleOption != null)
                yield return singleOption;
        }
        
        // 获取多选选项
        private FloatMenuOption GetMultiselectBoardMechOption(Thing clickedThing, FloatMenuContext context)
        {
            tmpPawns.Clear();
            
            var comp = clickedThing.TryGetComp<CompMechCrewHolder>();
            if (comp == null)
                return null;
            
            // 收集所有可以登机的Pawn
            foreach (var pawn in context.ValidSelectedPawns)
            {
                if (CanPawnBoardMech(pawn, clickedThing, comp))
                {
                    tmpPawns.Add(pawn);
                }
            }
            
            if (tmpPawns.Count == 0)
                return null;
            
            // 检查是否有机甲已满的情况
            string failStr = null;
            if (comp.IsFull)
            {
                failStr = "WULA_MechFull".Translate();
            }
            
            if (!failStr.NullOrEmpty())
            {
                return new FloatMenuOption(
                    "WULA_BoardMech".Translate(clickedThing.LabelShort) + ": " + failStr,
                    null,
                    MenuOptionPriority.DisabledOption
                );
            }
            
            // 计算可以登机的数量
            int canBoardCount = 0;
            foreach (var pawn in tmpPawns)
            {
                if (comp.HasRoom)
                {
                    canBoardCount++;
                }
                else
                {
                    break;
                }
            }

            string label = "WULA_BoardMech".Translate(clickedThing.LabelShort);
            
            return new FloatMenuOption(label, () =>
            {
                FleckMaker.Static(clickedThing.DrawPos, clickedThing.MapHeld, FleckDefOf.FeedbackEquip);
                
                // 为每个可以登机的Pawn创建Job
                foreach (var pawn in tmpPawns)
                {
                    if (comp.HasRoom && CanPawnBoardMech(pawn, clickedThing, comp))
                    {
                        Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_BoardMech, clickedThing);
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                }
            }, MenuOptionPriority.High);
        }
        
        // 获取单个选项
        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing == null || context.FirstSelectedPawn == null)
                return null;
                
            // 检查是否有乘员组件
            var comp = clickedThing.TryGetComp<CompMechCrewHolder>();
            if (comp == null)
                return null;
                
            // 创建菜单选项
            return CreateBoardMechOption(clickedThing, context.FirstSelectedPawn, comp);
        }
        
        // 创建登机菜单选项
        private FloatMenuOption CreateBoardMechOption(Thing mech, Pawn pawn, CompMechCrewHolder comp)
        {
            string label = "WULA_BoardMech".Translate(mech.LabelShort);
            string disabledReason = "";
            
            // 检查条件
            bool canBoard = CanPawnBoardMech(pawn, mech, comp, ref disabledReason);
            
            if (canBoard)
            {
                return new FloatMenuOption(label, () =>
                {
                    // 创建登机工作
                    Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_BoardMech, mech);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    
                    // 播放音效
                    FleckMaker.Static(mech.DrawPos, mech.MapHeld, FleckDefOf.FeedbackEquip);
                }, MenuOptionPriority.High);
            }
            else
            {
                return new FloatMenuOption(
                    label + ": " + disabledReason,
                    null,
                    MenuOptionPriority.DisabledOption);
            }
        }
        
        // 检查是否可以登机（带原因）
        private bool CanPawnBoardMech(Pawn pawn, Thing mech, CompMechCrewHolder comp, ref string disabledReason)
        {
            // 检查机甲是否已满
            if (comp.IsFull)
            {
                disabledReason = "WULA_MechCrewFull".Translate();
                return false;
            }
            
            // 检查Pawn是否可以成为乘员
            if (!comp.CanAddCrew(pawn))
            {
                disabledReason = "WULA_CannotBecomeCrew".Translate();
                return false;
            }
            
            // 检查Pawn是否已经在机甲内
            if (IsPawnInMech(pawn, mech))
            {
                disabledReason = "AlreadyInMech".Translate();
                return false;
            }
            
            // 检查距离
            if (!pawn.CanReach(mech, PathEndMode.Touch, Danger.Deadly))
            {
                disabledReason = "NoPath".Translate();
                return false;
            }
            
            // 检查Pawn状态
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
            if (mech is Pawn mechPawn && mechPawn.Downed)
            {
                disabledReason = "Downed".Translate();
                return false;
            }
            
            if (mech is Pawn mechPawn2 && mechPawn2.Dead)
            {
                disabledReason = "Dead".Translate();
                return false;
            }
            
            // 检查是否被征召（乘员不能是征召状态）
            if (pawn.Drafted)
            {
                disabledReason = "Drafted".Translate();
                return false;
            }
            
            return true;
        }
        
        // 检查是否可以登机（简化版）
        private bool CanPawnBoardMech(Pawn pawn, Thing mech, CompMechCrewHolder comp)
        {
            string disabledReason = "";
            return CanPawnBoardMech(pawn, mech, comp, ref disabledReason);
        }
    }
}
