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

        public MechWorkModeDef initialWorkMode;

        public CompProperties_AutonomousMech()
        {
            compClass = typeof(CompAutonomousMech);
        }
    }

    public class CompAutonomousMech : ThingComp
    {
        public CompProperties_AutonomousMech Props => (CompProperties_AutonomousMech)props;

        public Pawn MechPawn => parent as Pawn;

        private MechWorkModeDef currentWorkMode;

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

        public MechWorkModeDef CurrentWorkMode => currentWorkMode;

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
                currentWorkMode = Props.initialWorkMode ?? MechWorkModeDefOf.Work;
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
                DroneGizmo droneGizmo = new DroneGizmo(this);
                if (droneGizmo != null)
                {
                    yield return droneGizmo;
                }
            }
            // 更换武器按钮 - 确保不返回null
            Gizmo weaponSwitchGizmo = CreateWeaponSwitchGizmo();
            if (weaponSwitchGizmo != null)
            {
                yield return weaponSwitchGizmo;
            }
        }

        /// <summary>
        /// 创建更换武器的Gizmo
        /// </summary>
        private Gizmo CreateWeaponSwitchGizmo()
        {
            try
            {
                // 检查Pawn是否属于玩家派系
                if (MechPawn?.Faction != Faction.OfPlayer)
                {
                    return null; // 非玩家派系时不显示
                }
                // 检查Pawn是否有效
                if (MechPawn == null || MechPawn.Dead || MechPawn.Destroyed)
                {
                    return null;
                }
                // 检查equipment是否有效
                if (MechPawn.equipment == null)
                {
                    return null;
                }
                Command_Action switchWeaponCommand = new Command_Action
                {
                    defaultLabel = "WULA_SwitchWeapon".Translate(),
                    defaultDesc = "WULA_SwitchWeapon_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Abilities/WULA_WeaponSwitchAbility", false) ?? BaseContent.BadTex,
                    action = SwitchWeapon,
                };
                // 确保Command不为null
                if (switchWeaponCommand == null)
                {
                    WulaLog.Debug($"Failed to create weapon switch gizmo for {MechPawn?.LabelCap}");
                    return null;
                }
                return switchWeaponCommand;
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"Error creating weapon switch gizmo: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 更换武器逻辑
        /// </summary>
        private void SwitchWeapon()
        {
            if (MechPawn == null || MechPawn.Destroyed || !MechPawn.Spawned)
                return;

            try
            {
                // 1. 扔掉当前武器
                ThingWithComps currentWeapon = MechPawn.equipment?.Primary;
                if (currentWeapon != null)
                {
                    // 将武器扔在地上
                    MechPawn.equipment.TryDropEquipment(currentWeapon, out ThingWithComps droppedWeapon, MechPawn.Position, true);
                    
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug($"[CompAutonomousMech] {MechPawn.LabelCap} dropped weapon: {currentWeapon.LabelCap}");
                    }
                }

                // 2. 从PawnKind允许的武器中生成新武器
                ThingDef newWeaponDef = GetRandomWeaponFromPawnKind();
                if (newWeaponDef != null)
                {
                    // 生成新武器
                    Thing newWeapon = ThingMaker.MakeThing(newWeaponDef);
                    if (newWeapon is ThingWithComps newWeaponWithComps)
                    {
                        // 使用 AddEquipment 方法装备新武器
                        MechPawn.equipment.AddEquipment(newWeaponWithComps);
                        
                        Messages.Message("WULA_WeaponSwitched".Translate(MechPawn.LabelCap, newWeaponDef.LabelCap), 
                            MechPawn, MessageTypeDefOf.PositiveEvent);
                        
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"[CompAutonomousMech] {MechPawn.LabelCap} equipped new weapon: {newWeaponDef.LabelCap}");
                        }
                    }
                }
                else
                {
                    Messages.Message("WULA_NoWeaponAvailable".Translate(MechPawn.LabelCap), 
                        MechPawn, MessageTypeDefOf.NegativeEvent);
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CompAutonomousMech] Error switching weapon for {MechPawn?.LabelCap}: {ex}");
            }
        }

        /// <summary>
        /// 从PawnKind允许的武器中随机获取一个武器定义
        /// </summary>
        private ThingDef GetRandomWeaponFromPawnKind()
        {
            if (MechPawn.kindDef?.weaponTags == null || MechPawn.kindDef.weaponTags.Count == 0)
                return null;

            // 收集所有匹配的武器
            List<ThingDef> availableWeapons = new List<ThingDef>();
            
            foreach (string weaponTag in MechPawn.kindDef.weaponTags)
            {
                foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                {
                    if (thingDef.IsWeapon && thingDef.weaponTags != null && thingDef.weaponTags.Contains(weaponTag))
                    {
                        availableWeapons.Add(thingDef);
                    }
                }
            }

            if (availableWeapons.Count == 0)
                return null;

            // 随机选择一个武器
            return availableWeapons.RandomElement();
        }

        public void SetWorkMode(MechWorkModeDef mode)
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
