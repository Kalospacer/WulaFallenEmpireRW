using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    // 自定义条件节点：检查是否处于自主工作模式
    public class ThinkNode_ConditionalAutonomousMech : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;

            // 检查是否被征召
            if (pawn.Drafted)
                return false;

            // 检查是否有机械师控制
            if (pawn.GetOverseer() != null)
                return false;

            // 检查是否有自主能力
            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp == null || !comp.CanWorkAutonomously)
                return false;

            return true;
        }
    }

    // 新增：自主工作模式枚举
    public enum AutonomousWorkMode
    {
        Work,       // 工作模式：通过 thinktree 寻找工作
        Recharge,   // 充电模式：优先充电，完成后休眠
        Shutdown    // 关机模式：立即休眠
    }

    public class CompProperties_AutonomousMech : CompProperties
    {
        public bool enableAutonomousDrafting = true;
        public bool enableAutonomousWork = true;
        public bool requirePowerForAutonomy = true;
        public bool suppressUncontrolledWarning = true;
        
        public CompProperties_AutonomousMech()
        {
            compClass = typeof(CompAutonomousMech);
        }
    }

    public class CompAutonomousMech : ThingComp
    {
        public CompProperties_AutonomousMech Props => (CompProperties_AutonomousMech)props;
        
        public Pawn MechPawn => parent as Pawn;
        
        // 新增：当前工作模式
        private AutonomousWorkMode currentWorkMode = AutonomousWorkMode.Work;
        
        public bool CanBeAutonomous
        {
            get
            {
                if (MechPawn == null || MechPawn.Dead || MechPawn.Downed)
                    return false;
                    
                if (!Props.enableAutonomousDrafting)
                    return false;
                    
                if (MechPawn.GetOverseer() != null)
                    return false;
                    
                if (Props.requirePowerForAutonomy)
                {
                    var energyNeed = MechPawn.needs?.TryGetNeed<Need_MechEnergy>();
                    if (energyNeed != null && energyNeed.CurLevelPercentage < 0.1f)
                        return false;
                }
                
                return true;
            }
        }
        
        public bool CanWorkAutonomously
        {
            get
            {
                if (!Props.enableAutonomousWork)
                    return false;
                    
                if (!CanBeAutonomous)
                    return false;
                    
                if (MechPawn.Drafted)
                    return false;
                    
                return true;
            }
        }
        
        public bool ShouldSuppressUncontrolledWarning
        {
            get
            {
                if (!Props.suppressUncontrolledWarning)
                    return false;
                    
                return CanBeAutonomous;
            }
        }

        // 新增：公开访问当前工作模式
        public AutonomousWorkMode CurrentWorkMode => currentWorkMode;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (MechPawn != null && MechPawn.IsColonyMech)
            {
                EnsureWorkSettings();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (MechPawn == null || !CanBeAutonomous)
                yield break;

            // 自主征召按钮
            yield return new Command_Toggle
            {
                defaultLabel = "Autonomous Mode",
                defaultDesc = "Enable autonomous operation without mechanitor control",
                icon = TexCommand.Draft,
                isActive = () => MechPawn.Drafted,
                toggleAction = () => ToggleAutonomousDraft(),
                hotKey = KeyBindingDefOf.Misc1
            };

            // 工作模式切换按钮
            if (CanWorkAutonomously)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Work Mode: " + GetCurrentWorkModeDisplay(),
                    defaultDesc = "Switch autonomous work mode",
                    icon = TexCommand.Attack,
                    action = () => ShowWorkModeMenu()
                };
            }
        }

        private void ToggleAutonomousDraft()
        {
            if (MechPawn.drafter == null)
                return;

            if (MechPawn.Drafted)
            {
                MechPawn.drafter.Drafted = false;
                Messages.Message($"{MechPawn.LabelCap} autonomous mode deactivated", 
                    MechPawn, MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                if (CanBeAutonomous)
                {
                    MechPawn.drafter.Drafted = true;
                    Messages.Message($"{MechPawn.LabelCap} is now operating autonomously", 
                        MechPawn, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message($"Cannot activate autonomous mode: {GetBlockReason()}", 
                        MechPawn, MessageTypeDefOf.NegativeEvent);
                }
            }
        }

        // 修改：返回自定义工作模式的显示名称
        private string GetCurrentWorkModeDisplay()
        {
            switch (currentWorkMode)
            {
                case AutonomousWorkMode.Work:
                    return "Work";
                case AutonomousWorkMode.Recharge:
                    return "Recharge";
                case AutonomousWorkMode.Shutdown:
                    return "Shutdown";
                default:
                    return "Unknown";
            }
        }

        private void ShowWorkModeMenu()
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();
            
            // 工作模式
            list.Add(new FloatMenuOption("Work Mode - Perform assigned work tasks", 
                () => SetWorkMode(AutonomousWorkMode.Work)));
            
            // 充电模式
            list.Add(new FloatMenuOption("Recharge Mode - Charge and then shutdown", 
                () => SetWorkMode(AutonomousWorkMode.Recharge)));
            
            // 休眠模式
            list.Add(new FloatMenuOption("Shutdown Mode - Immediately shutdown", 
                () => SetWorkMode(AutonomousWorkMode.Shutdown)));

            Find.WindowStack.Add(new FloatMenu(list));
        }

        // 修改：设置自定义工作模式
        private void SetWorkMode(AutonomousWorkMode mode)
        {
            currentWorkMode = mode;
            
            // 清除当前工作，让机械族重新选择符合新模式的工作
            if (MechPawn.CurJob != null && MechPawn.CurJob.def != JobDefOf.Wait_Combat)
            {
                MechPawn.jobs.StopAll();
            }
            
            string modeName = GetCurrentWorkModeDisplay();
            Messages.Message($"{MechPawn.LabelCap} switched to {modeName} mode", 
                MechPawn, MessageTypeDefOf.NeutralEvent);
                
            Log.Message($"AutonomousMech: {MechPawn.LabelCap} work mode set to {modeName}");
        }

        private string GetBlockReason()
        {
            if (MechPawn.Dead || MechPawn.Downed)
                return "Mech is incapacitated";
                
            if (MechPawn.GetOverseer() != null)
                return "Mech is under mechanitor control";
                
            if (Props.requirePowerForAutonomy)
            {
                var energyNeed = MechPawn.needs?.TryGetNeed<Need_MechEnergy>();
                if (energyNeed != null && energyNeed.CurLevelPercentage < 0.1f)
                    return "Insufficient energy";
            }
                
            return "Autonomous mode disabled";
        }

        private void EnsureWorkSettings()
        {
            if (MechPawn.workSettings == null)
            {
                MechPawn.workSettings = new Pawn_WorkSettings(MechPawn);
            }
        }

        public string GetAutonomousStatusString()
        {
            if (!CanBeAutonomous)
                return null;
                
            if (MechPawn.Drafted)
                return "Operating autonomously";
            else
                return $"Autonomous mode: {GetCurrentWorkModeDisplay()}";
        }
        
        // 新增：保存和加载工作模式
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentWorkMode, "currentWorkMode", AutonomousWorkMode.Work);
        }
    }
}
