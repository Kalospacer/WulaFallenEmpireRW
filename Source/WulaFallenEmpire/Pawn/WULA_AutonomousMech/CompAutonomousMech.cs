using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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

        // 保留能量管理设置供 ThinkNode 使用
        public float lowEnergyThreshold = 0.3f;      // 低能量阈值
        public float criticalEnergyThreshold = 0.1f; // 临界能量阈值
        public float rechargeCompleteThreshold = 0.9f; // 充电完成阈值

        public DroneWorkModeDef initialWorkMode;

        public CompProperties_AutonomousMech()
        {
            compClass = typeof(CompAutonomousMech);
        }
    }

    public class CompAutonomousMech : ThingComp
    {
        public CompProperties_AutonomousMech Props => (CompProperties_AutonomousMech)props;

        public Pawn MechPawn => parent as Pawn;

        private DroneWorkModeDef currentWorkMode;

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

        public DroneWorkModeDef CurrentWorkMode => currentWorkMode;

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

            if (currentWorkMode == null)
            {
                currentWorkMode = Props.initialWorkMode ?? WulaDefOf.Work;
            }

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
                // 删除了自动切换模式的 CheckEnergyStatus 调用
                EnsureWorkSettings();
            }
        }

        // 删除了整个 CheckEnergyStatus 方法，因为充电逻辑在 ThinkNode 中处理

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (MechPawn == null || !CanBeAutonomous)
                yield break;
                
            // 工作模式切换按钮
            if (CanWorkAutonomously)
            {
                yield return new DroneGizmo(this);
            }
        }

        public void SetWorkMode(DroneWorkModeDef mode)
        {
            currentWorkMode = mode;

            // 清除当前工作，让机械族重新选择符合新模式的工作
            if (MechPawn.CurJob != null && MechPawn.CurJob.def != JobDefOf.Wait_Combat)
            {
                MechPawn.jobs.StopAll();
            }

            Messages.Message("WULA_SwitchedToMode".Translate(MechPawn.LabelCap, mode.label),
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
                return "WULA_Autonomous_Mode".Translate(currentWorkMode?.label ?? "Unknown") + energyInfo;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref currentWorkMode, "currentWorkMode");
            // 删除了 wasLowEnergy 的序列化
        }
    }
}
