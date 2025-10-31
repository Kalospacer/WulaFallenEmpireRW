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
                    defaultLabel = "Work Mode: " + GetCurrentWorkMode(),
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

        private string GetCurrentWorkMode()
        {
            if (MechPawn.workSettings == null)
                return "None";

            // 检查当前激活的工作模式
            foreach (var workType in MechPawn.RaceProps.mechEnabledWorkTypes)
            {
                if (MechPawn.workSettings.GetPriority(workType) > 0)
                {
                    return workType.defName;
                }
            }
            
            return "None";
        }

        private void ShowWorkModeMenu()
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();
            
            // 工作模式
            list.Add(new FloatMenuOption("Work Mode", () => SetWorkMode("Work")));
            
            // 充电模式
            list.Add(new FloatMenuOption("Recharge Mode", () => SetWorkMode("Recharge")));
            
            // 休眠模式
            list.Add(new FloatMenuOption("Shutdown Mode", () => SetWorkMode("SelfShutdown")));

            Find.WindowStack.Add(new FloatMenu(list));
        }

        private void SetWorkMode(string mode)
        {
            if (MechPawn.workSettings == null)
                return;

            // 重置所有工作模式优先级
            foreach (var workType in MechPawn.RaceProps.mechEnabledWorkTypes)
            {
                MechPawn.workSettings.SetPriority(workType, 0);
            }

            // 设置选择的工作模式
            var targetMode = DefDatabase<WorkTypeDef>.GetNamedSilentFail(mode);
            if (targetMode != null)
            {
                MechPawn.workSettings.SetPriority(targetMode, 3);
                Messages.Message($"{MechPawn.LabelCap} switched to {mode} mode", 
                    MechPawn, MessageTypeDefOf.NeutralEvent);
            }
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
            
            if (MechPawn.RaceProps.mechEnabledWorkTypes != null)
            {
                // 默认设置为工作模式
                var workMode = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Work");
                if (workMode != null)
                {
                    MechPawn.workSettings.SetPriority(workMode, 3);
                }
            }
        }

        public string GetAutonomousStatusString()
        {
            if (!CanBeAutonomous)
                return null;
                
            if (MechPawn.Drafted)
                return "Operating autonomously";
            else
                return "Autonomous mode available";
        }
    }
}
