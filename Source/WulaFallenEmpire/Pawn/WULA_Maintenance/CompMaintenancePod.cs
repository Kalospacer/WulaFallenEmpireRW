using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompProperties_MaintenancePod : CompProperties
    {
        public SoundDef enterSound;
        public SoundDef exitSound;
        public EffecterDef operatingEffecter;

        // 时间相关
        public int baseDurationTicks = 60000; // 基础维护时间（1天）
        public float ticksPerNeedLevel = 120000f; // 每点需求降低需要的时间

        // 电力消耗
        public float powerConsumptionRunning = 250f;
        public float powerConsumptionIdle = 50f;

        // 组件消耗
        public float componentCostPerNeedLevel = 2f;
        public int baseComponentCost = 1;

        // 维护效果
        public float minNeedLevelToMaintain = 0.3f; // 低于此值才需要维护
        public float needLevelAfterCycle = 1.0f; // 维护后的需求水平
        public bool healInjuries = true; // 是否治疗损伤
        public bool healMissingParts = true; // 是否修复缺失部位
        public int maxInjuriesHealedPerCycle = 5; // 每次维护最多治疗的损伤数量
        public CompProperties_MaintenancePod()
        {
            compClass = typeof(CompMaintenancePod);
        }
    }

    [StaticConstructorOnStartup]
    public class CompMaintenancePod : ThingComp, IThingHolder
    {
        // ===================== Fields =====================
        private ThingOwner innerContainer;
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        private int ticksRemaining;
        private MaintenancePodState state = MaintenancePodState.Idle;
        private Effecter operatingEffecter;
        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        private static readonly Texture2D EnterIcon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject");
        // ===================== Properties =====================
        public CompProperties_MaintenancePod Props => (CompProperties_MaintenancePod)props;
        public MaintenancePodState State => state;
        public Pawn Occupant => innerContainer.FirstOrDefault() as Pawn;
        public bool PowerOn => powerComp != null && powerComp.PowerOn;
        public float RequiredComponents
        {
            get
            {
                var occupant = Occupant;
                if (occupant == null) return Props.baseComponentCost;

                var maintenanceNeed = occupant.needs?.TryGetNeed<Need_Maintenance>();
                if (maintenanceNeed == null) return Props.baseComponentCost;

                // 计算基于当前需求水平的组件需求
                float needDeficit = 1.0f - maintenanceNeed.CurLevel;
                return Props.baseComponentCost + (needDeficit * Props.componentCostPerNeedLevel);
            }
        }
        public int RequiredDuration
        {
            get
            {
                var occupant = Occupant;
                if (occupant == null) return Props.baseDurationTicks;

                var maintenanceNeed = occupant.needs?.TryGetNeed<Need_Maintenance>();
                if (maintenanceNeed == null) return Props.baseDurationTicks;

                // 计算基于当前需求水平的维护时间
                float needDeficit = 1.0f - maintenanceNeed.CurLevel;
                return Props.baseDurationTicks + (int)(needDeficit * Props.ticksPerNeedLevel);
            }
        }
        // ===================== Setup =====================
        public CompMaintenancePod()
        {
            innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPowerTrader>();
            refuelableComp = parent.TryGetComp<CompRefuelable>();
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref state, "state", MaintenancePodState.Idle);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (mode == DestroyMode.Deconstruct || mode == DestroyMode.KillFinalize)
            {
                EjectPawn();
            }
        }
        // ===================== IThingHolder Implementation =====================
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        // ===================== Core Logic =====================
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;
            // 更新电力消耗
            if (powerComp != null)
            {
                powerComp.PowerOutput = -(state == MaintenancePodState.Running ? Props.powerConsumptionRunning : Props.powerConsumptionIdle);
            }
            // 运行维护周期
            if (state == MaintenancePodState.Running && PowerOn)
            {
                ticksRemaining--;

                // 更新效果器
                if (Props.operatingEffecter != null)
                {
                    if (operatingEffecter == null)
                    {
                        operatingEffecter = Props.operatingEffecter.Spawn();
                    }
                    operatingEffecter.EffectTick(parent, Occupant);
                }
                if (ticksRemaining <= 0)
                {
                    CycleFinished();
                }
            }
            else if (operatingEffecter != null)
            {
                operatingEffecter.Cleanup();
                operatingEffecter = null;
            }
        }
        public void StartCycle(Pawn pawn)
        {
            if (pawn == null) return;
            // 检查组件是否足够
            float requiredComponents = RequiredComponents;
            if (refuelableComp.Fuel < requiredComponents)
            {
                Messages.Message("WULA_MaintenancePod_NotEnoughComponents".Translate(requiredComponents.ToString("F0")), MessageTypeDefOf.RejectInput);
                return;
            }
            // 消耗组件
            if (requiredComponents > 0)
            {
                refuelableComp.ConsumeFuel(requiredComponents);
            }
            // 将 pawn 放入容器
            if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
            }
            innerContainer.TryAddOrTransfer(pawn);
            // 开始维护周期
            state = MaintenancePodState.Running;
            ticksRemaining = RequiredDuration;

            // 播放进入音效
            if (Props.enterSound != null)
            {
                Props.enterSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
            Messages.Message("WULA_MaintenanceCycleStarted".Translate(pawn.LabelShortCap), MessageTypeDefOf.PositiveEvent);
        }
        private void CycleFinished()
        {
            var occupant = Occupant;
            if (occupant == null)
            {
                state = MaintenancePodState.Idle;
                return;
            }
            // 执行维护效果
            PerformMaintenanceEffects(occupant);

            // 弹出 pawn
            EjectPawn();

            Messages.Message("WULA_MaintenanceCycleComplete".Translate(occupant.LabelShortCap), MessageTypeDefOf.PositiveEvent);
        }
        private void PerformMaintenanceEffects(Pawn pawn)
        {
            var maintenanceNeed = pawn.needs?.TryGetNeed<Need_Maintenance>();

            // 1. 恢复维护需求
            if (maintenanceNeed != null)
            {
                maintenanceNeed.PerformMaintenance(Props.needLevelAfterCycle);
            }
            // 2. 治疗损伤（如果启用）
            if (Props.healInjuries)
            {
                HealInjuries(pawn);
            }
            // 3. 修复缺失部位（如果启用）
            if (Props.healMissingParts)
            {
                HealMissingParts(pawn);
            }
        }
        private void HealInjuries(Pawn pawn)
        {
            int injuriesHealed = 0;
            var injuries = pawn.health.hediffSet.hediffs
                .Where(h => h.def.isBad && h.Visible && h.def != HediffDefOf.BloodLoss)
                .ToList();
            foreach (var injury in injuries)
            {
                if (injuriesHealed >= Props.maxInjuriesHealedPerCycle)
                    break;
                pawn.health.RemoveHediff(injury);
                injuriesHealed++;
            }
            if (injuriesHealed > 0)
            {
                Messages.Message("WULA_MaintenanceHealedInjuries".Translate(pawn.LabelShortCap, injuriesHealed), MessageTypeDefOf.PositiveEvent);
            }
        }
        private void HealMissingParts(Pawn pawn)
        {
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            int partsHealed = 0;
            foreach (var missingPart in missingParts)
            {
                if (partsHealed >= 1) // 每次最多修复一个缺失部位
                    break;
                pawn.health.RemoveHediff(missingPart);
                partsHealed++;
            }
            if (partsHealed > 0)
            {
                Messages.Message("WULA_MaintenanceHealedParts".Translate(pawn.LabelShortCap), MessageTypeDefOf.PositiveEvent);
            }
        }
        public void EjectPawn(bool interrupted = false)
        {
            var occupant = Occupant;
            if (occupant != null)
            {
                // 弹出到交互单元格
                innerContainer.TryDropAll(parent.InteractionCell, parent.Map, ThingPlaceMode.Near);

                // 播放退出音效
                if (Props.exitSound != null)
                {
                    Props.exitSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
                }
                // 如果被中断，应用负面效果
                if (interrupted)
                {
                    occupant.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.SoakingWet);
                }
            }
            innerContainer.Clear();
            state = MaintenancePodState.Idle;

            // 清理效果器
            if (operatingEffecter != null)
            {
                operatingEffecter.Cleanup();
                operatingEffecter = null;
            }
        }
        // ===================== UI & Gizmos =====================
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WULA_MaintenancePod_Status".Translate() + ": " + $"WULA_MaintenancePod_State_{state}".Translate());
            if (state == MaintenancePodState.Running && Occupant != null)
            {
                sb.AppendLine("Contains".Translate() + ": " + Occupant.NameShortColored.Resolve());
                sb.AppendLine("TimeLeft".Translate() + ": " + ticksRemaining.ToStringTicksToPeriod());
                var maintenanceNeed = Occupant.needs?.TryGetNeed<Need_Maintenance>();
                if (maintenanceNeed != null)
                {
                    // 直接显示 CurLevel，确保与 Need 显示一致
                    sb.AppendLine("WULA_MaintenanceLevel".Translate() + ": " + maintenanceNeed.CurLevel.ToStringPercent());
                }
            }
            if (!PowerOn)
            {
                sb.AppendLine("NoPower".Translate().Colorize(Color.red));
            }
            return sb.ToString().TrimEnd();
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            // 进入维护舱的按钮
            if (state == MaintenancePodState.Idle && PowerOn)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_MaintenancePod_Enter".Translate(),
                    defaultDesc = "WULA_MaintenancePod_EnterDesc".Translate(),
                    icon = EnterIcon,
                    action = () => ShowPawnSelectionMenu()
                };
            }
            // 取消维护的按钮
            if (state == MaintenancePodState.Running)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CommandCancelConstructionLabel".Translate(),
                    defaultDesc = "WULA_MaintenancePod_CancelDesc".Translate(),
                    icon = CancelIcon,
                    action = () =>
                    {
                        EjectPawn(true);
                        Messages.Message("WULA_MaintenanceCanceled".Translate(), MessageTypeDefOf.NegativeEvent);
                    }
                };
            }
        }
        private void ShowPawnSelectionMenu()
        {
            var options = GetPawnOptions();
            if (options.Any())
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
            else
            {
                Messages.Message("WULA_MaintenancePod_NoOneNeeds".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
        private List<FloatMenuOption> GetPawnOptions()
        {
            var options = new List<FloatMenuOption>();
            var map = parent.Map;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                // 首先检查是否有维护需求
                var maintenanceNeed = pawn.needs?.TryGetNeed<Need_Maintenance>();
                if (maintenanceNeed == null)
                {
                    // 这个Pawn没有维护需求，跳过
                    continue;
                }

                // 检查是否真的需要维护
                if (maintenanceNeed.CurLevel > Props.minNeedLevelToMaintain && !DebugSettings.godMode)
                    continue;

                // 创建选项
                var option = CreatePawnOption(pawn, maintenanceNeed);
                if (option != null)
                    options.Add(option);
            }

            return options;
        }

        private FloatMenuOption CreatePawnOption(Pawn pawn, Need_Maintenance need)
        {
            string label = $"{pawn.LabelShortCap} ({need.CurLevel.ToStringPercent()})";
            float requiredComponents = RequiredComponents;
            // 检查组件是否足够
            if (refuelableComp.Fuel < requiredComponents)
            {
                return new FloatMenuOption(label + " (" + "WULA_MaintenancePod_NotEnoughComponents".Translate(requiredComponents.ToString("F0")) + ")", null);
            }
            // 检查是否可以到达
            if (!pawn.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                return new FloatMenuOption(label + " (" + "CannotReach".Translate() + ")", null);
            }
            return new FloatMenuOption(label, () =>
            {
                if (pawn.Downed || !pawn.IsFreeColonist)
                {
                    // 需要搬运
                    var haulJob = JobMaker.MakeJob(JobDefOf_WULA.WULA_HaulToMaintenancePod, pawn, parent);
                    var hauler = FindBestHauler(pawn);
                    if (hauler != null)
                    {
                        hauler.jobs.TryTakeOrderedJob(haulJob);
                    }
                    else
                    {
                        Messages.Message("WULA_NoHaulerAvailable".Translate(), MessageTypeDefOf.RejectInput);
                    }
                }
                else
                {
                    // 自己进入
                    var enterJob = JobMaker.MakeJob(JobDefOf_WULA.WULA_EnterMaintenancePod, parent);
                    pawn.jobs.TryTakeOrderedJob(enterJob);
                }
            });
        }
        private Pawn FindBestHauler(Pawn target)
        {
            return parent.Map.mapPawns.FreeColonistsSpawned
                .Where(colonist => !colonist.Downed &&
                      colonist.CanReserveAndReach(target, PathEndMode.OnCell, Danger.Deadly) &&
                      colonist.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(colonist => colonist.Position.DistanceTo(target.Position))
                .FirstOrDefault();
        }
    }
    public enum MaintenancePodState
    {
        Idle,
        Running
    }
}