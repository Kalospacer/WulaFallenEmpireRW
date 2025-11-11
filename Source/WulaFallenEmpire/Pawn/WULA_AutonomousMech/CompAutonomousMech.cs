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

        // 新增：能量管理设置
        public float lowEnergyThreshold = 0.3f;      // 低能量阈值
        public float criticalEnergyThreshold = 0.1f; // 临界能量阈值
        public float rechargeCompleteThreshold = 0.9f; // 充电完成阈值

        public CompProperties_AutonomousMech()
        {
            compClass = typeof(CompAutonomousMech);
        }
    }

    public class CompAutonomousMech : ThingComp
    {
        public CompProperties_AutonomousMech Props => (CompProperties_AutonomousMech)props;

        public Pawn MechPawn => parent as Pawn;

        private AutonomousWorkMode currentWorkMode = AutonomousWorkMode.Work;
        private bool wasLowEnergy = false; // 记录上次是否处于低能量状态

        public bool CanBeAutonomous
        {
            get
            {
                if (MechPawn == null || MechPawn.Dead )
                    return false;

                if (!Props.enableAutonomousDrafting)
                    return false;

                if (MechPawn.GetOverseer() != null)
                    return false;

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
        public bool IsInCombatMode
        {
            get
            {
                if (MechPawn == null || MechPawn.Dead || MechPawn.Downed)
                    return false;
                // 被征召或处于自主战斗模式
                return MechPawn.Drafted || (CanFightAutonomously && MechPawn.mindState?.duty?.def == DutyDefOf.AssaultColony);
            }
        }

        // 在 CompAutonomousMech 类中添加这个新属性
        public bool CanFightAutonomously
        {
            get
            {
                if (MechPawn == null || MechPawn.Dead || MechPawn.Downed)
                    return false;

                if (!Props.enableAutonomousDrafting)
                    return false;

                if (MechPawn.GetOverseer() != null)
                    return false;

                if (!MechPawn.drafter?.Drafted == true)
                    return false;

                if (Props.requirePowerForAutonomy)
                {
                    if (GetEnergyLevel() < Props.criticalEnergyThreshold)
                        return false;
                }

                return true;
            }
        }

        public AutonomousWorkMode CurrentWorkMode => currentWorkMode;

        // 新增：能量状态检查方法
        public float GetEnergyLevel()
        {
            var energyNeed = MechPawn.needs?.TryGetNeed<Need_MechEnergy>();
            return energyNeed?.CurLevelPercentage ?? 0f;
        }

        public bool IsLowEnergy => GetEnergyLevel() < Props.lowEnergyThreshold;
        public bool IsCriticalEnergy => GetEnergyLevel() < Props.criticalEnergyThreshold;
        public bool IsFullyCharged => GetEnergyLevel() >= Props.rechargeCompleteThreshold;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 确保使用独立战斗系统
            InitializeAutonomousCombat();
        }

        private void InitializeAutonomousCombat()
        {
            // 确保有 draftController
            if (MechPawn.drafter == null)
            {
                MechPawn.drafter = new Pawn_DraftController(MechPawn);
            }

            // 强制启用 FireAtWill
            if (MechPawn.drafter != null)
            {
                MechPawn.drafter.FireAtWill = true;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            // 每60 tick检查一次能量状态
            if (MechPawn != null && MechPawn.IsColonyMech && Find.TickManager.TicksGame % 60 == 0)
            {
                CheckEnergyStatus();
                EnsureWorkSettings();
            }
        }

        // 新增：能量状态检查
        private void CheckEnergyStatus()
        {
            if (!CanWorkAutonomously)
                return;
            bool isLowEnergyNow = IsLowEnergy;

            // 如果能量状态发生变化
            if (isLowEnergyNow != wasLowEnergy)
            {
                if (isLowEnergyNow)
                {
                    // 进入低能量状态
                    if (currentWorkMode == AutonomousWorkMode.Work)
                    {
                        // 自动切换到充电模式
                        SetWorkMode(AutonomousWorkMode.Recharge);
                        Messages.Message("WULA_LowEnergySwitchToRecharge".Translate(MechPawn.LabelCap),
                            MechPawn, MessageTypeDefOf.CautionInput);
                    }
                }
                else
                {
                    // 恢复能量状态
                    if (currentWorkMode == AutonomousWorkMode.Recharge && IsFullyCharged)
                    {
                        // 充满电后自动切换回工作模式
                        SetWorkMode(AutonomousWorkMode.Work);
                        Messages.Message("WULA_FullyChargedSwitchToWork".Translate(MechPawn.LabelCap),
                            MechPawn, MessageTypeDefOf.PositiveEvent);
                    }
                }

                wasLowEnergy = isLowEnergyNow;
            }

            // 临界能量警告
            if (IsCriticalEnergy && currentWorkMode != AutonomousWorkMode.Recharge && currentWorkMode != AutonomousWorkMode.Shutdown)
            {
                Messages.Message("WULA_CriticalEnergyLevels".Translate(MechPawn.LabelCap),
                    MechPawn, MessageTypeDefOf.ThreatBig);
                // 强制切换到充电模式
                SetWorkMode(AutonomousWorkMode.Recharge);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (MechPawn == null || !CanBeAutonomous)
                yield break;
                
            // 工作模式切换按钮
            if (CanWorkAutonomously)
            {
                string energyInfo = "WULA_EnergyInfo".Translate(GetEnergyLevel().ToStringPercent());
                yield return new Command_Action
                {
                    defaultLabel = "WULA_Mech_WorkMode".Translate(GetCurrentWorkModeDisplay()) + energyInfo,
                    defaultDesc = GetWorkModeDescription(),
                    icon = GetWorkModeIcon(),
                    action = () => ShowWorkModeMenu()
                };
            }
        }

        // 修改：返回包含能量信息的描述
        private string GetWorkModeDescription()
        {
            string baseDesc = "WULA_Switch_Mech_WorkMode".Translate();
            string energyInfo = "WULA_CurrentEnergy".Translate(GetEnergyLevel().ToStringPercent());

            if (IsLowEnergy)
                energyInfo += "WULA_EnergyLow".Translate();
            if (IsCriticalEnergy)
                energyInfo += "WULA_EnergyCritical".Translate();

            return baseDesc + "\n" + energyInfo;
        }

        // 新增：根据能量状态返回不同的图标
        private UnityEngine.Texture2D GetWorkModeIcon()
        {
            if (IsCriticalEnergy)
                return TexCommand.DesirePower;
            else if (IsLowEnergy)
                return TexCommand.ToggleVent;
            else
                return TexCommand.Attack;
        }

        private string GetCurrentWorkModeDisplay()
        {
            switch (currentWorkMode)
            {
                case AutonomousWorkMode.Work:
                    return "WULA_WorkMode_Work".Translate();
                case AutonomousWorkMode.Recharge:
                    return "WULA_WorkMode_Recharge".Translate();
                case AutonomousWorkMode.Shutdown:
                    return "WULA_WorkMode_Shutdown".Translate();
                default:
                    return "WULA_WorkMode_Unknown".Translate();
            }
        }

        private void ShowWorkModeMenu()
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();

            // 工作模式
            list.Add(new FloatMenuOption("WULA_WorkMode_Work_Desc".Translate(),
                () => SetWorkMode(AutonomousWorkMode.Work)));

            // 充电模式
            list.Add(new FloatMenuOption("WULA_WorkMode_Recharge_Desc".Translate(),
                () => SetWorkMode(AutonomousWorkMode.Recharge)));

            // 休眠模式
            list.Add(new FloatMenuOption("WULA_WorkMode_Shutdown_Desc".Translate(),
                () => SetWorkMode(AutonomousWorkMode.Shutdown)));

            Find.WindowStack.Add(new FloatMenu(list));
        }

        private void SetWorkMode(AutonomousWorkMode mode)
        {
            currentWorkMode = mode;

            // 清除当前工作，让机械族重新选择符合新模式的工作
            if (MechPawn.CurJob != null && MechPawn.CurJob.def != JobDefOf.Wait_Combat)
            {
                MechPawn.jobs.StopAll();
            }

            string modeName = GetCurrentWorkModeDisplay();
            Messages.Message("WULA_SwitchedToMode".Translate(MechPawn.LabelCap, modeName),
                MechPawn, MessageTypeDefOf.NeutralEvent);
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

            string energyInfo = "WULA_EnergyInfoShort".Translate(GetEnergyLevel().ToStringPercent());

            if (MechPawn.Drafted)
                return "WULA_Autonomous_Drafted".Translate() + energyInfo;
            else
                return "WULA_Autonomous_Mode".Translate(GetCurrentWorkModeDisplay()) + energyInfo;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentWorkMode, "currentWorkMode", AutonomousWorkMode.Work);
            Scribe_Values.Look(ref wasLowEnergy, "wasLowEnergy", false);
        }
    }
}