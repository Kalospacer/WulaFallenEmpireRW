using WulaFallenEmpire;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompProperties_MechPilotHolder : CompProperties
    {
        public int maxPilots = 1;
        public string pilotWorkTag = "MechPilot";

        // 图标配置
        public string summonPilotIcon = "Wula/UI/Commands/WULA_Enter_Mech";
        public string ejectPilotIcon = "Wula/UI/Commands/WULA_Exit_Mech";
        public string recallPilotIcon = "Wula/UI/Commands/WULA_Recall_Pilot";
        
        // 快速登机配置
        public bool enableQuickRecall = true;
        public float recallRadius = 30f;

        public float ejectPilotHealthPercentThreshold = 0.1f;
        public bool allowEntryBelowThreshold = false;

        // Hediff同步配置
        public bool syncPilotHediffs = true;
        public List<string> syncedHediffDefs = null;
        public bool autoApplyHediffOnEntry = false;
        public HediffDef autoHediffDef = null;
        public float autoHediffSeverity = 0.5f;

        public CompProperties_MechPilotHolder()
        {
            this.compClass = typeof(CompMechPilotHolder);
        }

        // 图标加载方法
        public Texture2D GetSummonPilotIcon()
        {
            if (!string.IsNullOrEmpty(summonPilotIcon) && ContentFinder<Texture2D>.Get(summonPilotIcon, false) != null)
            {
                return ContentFinder<Texture2D>.Get(summonPilotIcon);
            }
            return ContentFinder<Texture2D>.Get("UI/Commands/SummonPilot", false) ??
                   BaseContent.BadTex;
        }

        public Texture2D GetEjectPilotIcon()
        {
            if (!string.IsNullOrEmpty(ejectPilotIcon) && ContentFinder<Texture2D>.Get(ejectPilotIcon, false) != null)
            {
                return ContentFinder<Texture2D>.Get(ejectPilotIcon);
            }
            return ContentFinder<Texture2D>.Get("UI/Commands/Eject", false) ??
                   BaseContent.BadTex;
        }
        
        public Texture2D GetRecallPilotIcon()
        {
            if (!string.IsNullOrEmpty(recallPilotIcon) && ContentFinder<Texture2D>.Get(recallPilotIcon, false) != null)
            {
                return ContentFinder<Texture2D>.Get(recallPilotIcon);
            }
            return ContentFinder<Texture2D>.Get("UI/Commands/SummonPilot", false) ??
                   BaseContent.BadTex;
        }
    }

    public class CompMechPilotHolder : ThingComp, IThingHolder, ISuspendableThingHolder
    {
        public ThingOwner innerContainer;

        private bool isProcessingDestruction = false;
        private bool hasEjectedDueToLowHealth = false;
        private Dictionary<Pawn, List<Hediff>> syncedHediffs = new Dictionary<Pawn, List<Hediff>>();
        
        // 新增：记录上一次的驾驶员
        private Pawn lastPilot = null;

        public CompProperties_MechPilotHolder Props => (CompProperties_MechPilotHolder)props;

        public int CurrentPilotCount => innerContainer.Count;
        public bool HasPilots => innerContainer.Count > 0;
        public bool HasRoom => innerContainer.Count < Props.maxPilots;
        public bool IsFull => innerContainer.Count >= Props.maxPilots;

        public bool IsContentsSuspended => true;

        // 精神状态定义
        private MentalStateDef MechNoPilotStateDef => WULA_MentalStateDefOf.WULA_MechNoPilot;

        private void CheckAndUpdateMentalState()
        {
            var mech = parent as Pawn;
            if (mech == null || mech.Dead || MechNoPilotStateDef == null)
                return;

            if (!HasPilots)
            {
                if (mech.MentalStateDef != MechNoPilotStateDef && !mech.InMentalState)
                {
                    mech.mindState.mentalStateHandler.TryStartMentalState(MechNoPilotStateDef, null, true);
                }
            }
            else
            {
                if (mech.MentalStateDef == MechNoPilotStateDef)
                {
                    mech.mindState.mentalStateHandler.CurState?.RecoverFromState();
                }
            }
        }

        // 添加驾驶员
        public void AddPilot(Pawn pawn)
        {
            if (!CanAddPilot(pawn))
                return;

            // 记录驾驶员
            if (lastPilot != pawn)
            {
                lastPilot = pawn;
            }

            if (pawn.Spawned)
                pawn.DeSpawnOrDeselect();

            innerContainer.TryAdd(pawn, true);

            pawn.pather?.StopDead();
            pawn.jobs?.StopAll();

            Notify_PilotAdded(pawn);
            CheckAndUpdateMentalState();

            if (Props.syncPilotHediffs)
            {
                SyncPilotHediffs(pawn);
            }

            if (Props.autoApplyHediffOnEntry && Props.autoHediffDef != null)
            {
                AddAutoHediff(pawn);
            }
        }

        // 移除驾驶员
        public void RemovePilot(Pawn pawn, IntVec3? exitPos = null)
        {
            if (Props.syncPilotHediffs)
            {
                UnsyncPilotHediffs(pawn);
            }

            if (innerContainer.Contains(pawn))
            {
                innerContainer.Remove(pawn);
                TrySpawnPilotAtPosition(pawn, exitPos ?? parent.Position);
                Notify_PilotRemoved(pawn);
                StopMechJobs();
                CheckAndUpdateMentalState();
            }
        }

        private void SyncPilotHediffs(Pawn pawn)
        {
            if (pawn == null || !(parent is Wulamechunit mech))
                return;

            try
            {
                var hediffsToSync = new List<Hediff>();

                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (ShouldSyncHediff(hediff))
                    {
                        hediffsToSync.Add(hediff);
                        var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                        if (syncComp != null)
                        {
                            syncComp.OnPilotEnteredMech(mech);
                        }
                    }
                }

                if (hediffsToSync.Count > 0)
                {
                    syncedHediffs[pawn] = hediffsToSync;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 同步Hediff时出错: {ex}");
            }
        }

        private void UnsyncPilotHediffs(Pawn pawn)
        {
            if (pawn == null || !syncedHediffs.ContainsKey(pawn))
                return;

            try
            {
                foreach (var hediff in syncedHediffs[pawn])
                {
                    var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                    if (syncComp != null)
                    {
                        syncComp.OnPilotExitedMech();
                    }
                }

                syncedHediffs.Remove(pawn);
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 取消同步Hediff时出错: {ex}");
            }
        }

        private bool ShouldSyncHediff(Hediff hediff)
        {
            if (hediff == null)
                return false;

            var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
            if (syncComp == null)
                return false;

            if (Props.syncedHediffDefs != null && Props.syncedHediffDefs.Count > 0)
            {
                return Props.syncedHediffDefs.Contains(hediff.def.defName);
            }

            return true;
        }

        private void AddAutoHediff(Pawn pawn)
        {
            try
            {
                var existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.autoHediffDef);
                if (existingHediff == null)
                {
                    var hediff = HediffMaker.MakeHediff(Props.autoHediffDef, pawn);
                    hediff.Severity = Props.autoHediffSeverity;
                    pawn.health.AddHediff(hediff);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 自动添加Hediff时出错: {ex}");
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            try
            {
                if (Find.TickManager.TicksGame % 60 == 0)
                {
                    CheckLowHealth();
                    CheckAndUpdateMentalState();
                }

                if (Find.TickManager.TicksGame % 120 == 0)
                {
                    CheckHediffSync();
                }

                var mech = parent as Pawn;
                if (mech != null && mech.Dead && HasPilots)
                {
                    EjectAllPilotsOnDeath();
                    return;
                }

                var pilotsToRemove = new List<Pawn>();
                foreach (var thing in innerContainer)
                {
                    if (thing is Pawn pawn && pawn.Dead)
                    {
                        pilotsToRemove.Add(pawn);
                    }
                }

                foreach (var pawn in pilotsToRemove)
                {
                    RemovePilot(pawn);
                }

                foreach (var thing in innerContainer)
                {
                    if (thing is Pawn pawn)
                    {
                        pawn.jobs?.StopAll();
                        pawn.pather?.StopDead();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] CompTick error: {ex}");
            }
        }

        private void CheckHediffSync()
        {
            if (!Props.syncPilotHediffs || !(parent is Wulamechunit))
                return;

            try
            {
                foreach (var pilot in GetPilots())
                {
                    if (pilot == null || pilot.Dead || pilot.Destroyed)
                        continue;

                    SyncPilotHediffs(pilot);

                    if (syncedHediffs.ContainsKey(pilot))
                    {
                        var currentHediffs = pilot.health.hediffSet.hediffs
                            .Where(ShouldSyncHediff)
                            .ToList();

                        var removedHediffs = syncedHediffs[pilot]
                            .Where(h => !currentHediffs.Contains(h))
                            .ToList();

                        foreach (var hediff in removedHediffs)
                        {
                            var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                            if (syncComp != null)
                            {
                                syncComp.OnPilotExitedMech();
                            }
                        }

                        syncedHediffs[pilot] = currentHediffs;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 检查Hediff同步状态时出错: {ex}");
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!(parent is Wulamechunit))
            {
                Log.Warning($"[WULA] CompMechPilotHolder attached to non-mech: {parent}");
            }

            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Pawn>(this);
            }

            CheckAndUpdateMentalState();

            if (Props.syncPilotHediffs)
            {
                foreach (var pilot in GetPilots())
                {
                    SyncPilotHediffs(pilot);
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref isProcessingDestruction, "isProcessingDestruction", false);
            Scribe_Values.Look(ref hasEjectedDueToLowHealth, "hasEjectedDueToLowHealth", false);
            Scribe_Collections.Look(ref syncedHediffs, "syncedHediffs", LookMode.Reference, LookMode.Deep);
            Scribe_References.Look(ref lastPilot, "lastPilot");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CheckAndUpdateMentalState();
                
                if (Props.syncPilotHediffs)
                {
                    foreach (var pilot in GetPilots())
                    {
                        SyncPilotHediffs(pilot);
                    }
                }
            }
        }

        private void StopMechJobs()
        {
            var mech = parent as Pawn;
            if (mech == null)
                return;

            mech.jobs?.StopAll();
            mech.pather?.StopDead();

            var drafter = mech.drafter;
            if (drafter != null && mech.Drafted)
            {
                mech.drafter.Drafted = false;
            }

            mech.jobs?.ClearQueuedJobs();
            mech.mindState.enemyTarget = null;
        }

        public float CurrentHealthPercent
        {
            get
            {
                var mech = parent as Pawn;
                if (mech == null || mech.health == null)
                    return 1.0f;

                return mech.health.summaryHealth.SummaryHealthPercent;
            }
        }

        public bool IsBelowHealthThreshold => CurrentHealthPercent < Props.ejectPilotHealthPercentThreshold;

        public bool CanAddPilot(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;
            
            if (pawn.Downed)
                return true;
            
            if (!HasRoom)
                return false;
            if (innerContainer.Contains(pawn))
                return false;
                
            if (!string.IsNullOrEmpty(Props.pilotWorkTag))
            {
                WorkTags tag;
                if (System.Enum.TryParse(Props.pilotWorkTag, out tag))
                {
                    if (pawn.WorkTagIsDisabled(tag))
                        return false;
                }
            }

            if (!Props.allowEntryBelowThreshold && IsBelowHealthThreshold)
            {
                return false;
            }
            return true;
        }

        private bool CanPawnMoveToMech(Pawn pawn, Wulamechunit mech)
        {
            if (pawn == null || mech == null)
                return false;
            
            if (pawn.Downed)
                return false;
            
            return pawn.CanReach(mech, PathEndMode.Touch, Danger.Deadly);
        }

        private void CheckLowHealth()
        {
            if (IsBelowHealthThreshold && HasPilots)
            {
                EjectPilotsDueToLowHealth();
            }
            else if (!IsBelowHealthThreshold)
            {
                hasEjectedDueToLowHealth = false;
            }
        }

        private void EjectPilotsDueToLowHealth()
        {
            if (hasEjectedDueToLowHealth)
                return;

            RemoveAllPilots();

            if (parent.Faction == Faction.OfPlayer)
            {
                Messages.Message("WULA_PilotsEjectedDueToLowHealth".Translate(parent.LabelShort,
                    (Props.ejectPilotHealthPercentThreshold * 100).ToString("F0")),
                    parent, MessageTypeDefOf.NegativeEvent);
            }

            hasEjectedDueToLowHealth = true;
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);

            var mech = parent as Pawn;
            if (mech != null && mech.Dead)
            {
                EjectAllPilotsOnDeath();
            }
            else
            {
                CheckLowHealth();
            }
        }

        // 修改Gizmo显示：只在条件满足时显示快速登机按钮
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Wulamechunit mech) || mech.Faction != Faction.OfPlayer)
                yield break;

            // 快速登机按钮（只有在所有条件都满足时才显示）
            if (Props.enableQuickRecall && HasRoom && !IsBelowHealthThreshold)
            {
                var recallGizmo = CreateRecallGizmo();
                if (recallGizmo != null)
                {
                    yield return recallGizmo;
                }
            }

            // 召唤驾驶员按钮
            if (HasRoom)
            {
                Command_Action summonCommand = new Command_Action
                {
                    defaultLabel = "WULA_SummonPilot".Translate(),
                    defaultDesc = "WULA_SummonPilotDesc".Translate(),
                    icon = Props.GetSummonPilotIcon(),
                    action = () =>
                    {
                        ShowPilotSelectionMenu();
                    },
                    hotKey = KeyBindingDefOf.Misc2
                };

                if (!Props.allowEntryBelowThreshold && IsBelowHealthThreshold)
                {
                    summonCommand.Disable("WULA_MechTooDamagedForEntry".Translate());
                }

                yield return summonCommand;
            }

            // 弹出所有驾驶员按钮
            if (innerContainer.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_EjectAllPilots".Translate(),
                    defaultDesc = "WULA_EjectAllPilotsDesc".Translate(),
                    icon = Props.GetEjectPilotIcon(),
                    action = () =>
                    {
                        RemoveAllPilots();
                    },
                    hotKey = KeyBindingDefOf.Misc1
                };
            }
        }
        
        // 创建快速登机Gizmo（只有条件完全满足时才创建）
        private Command_Action CreateRecallGizmo()
        {
            // 检查是否有上次驾驶员
            if (lastPilot == null)
                return null;
                
            // 检查驾驶员是否可用
            if (!IsPilotAvailableForRecall(lastPilot))
                return null;
                
            // 创建并返回Gizmo
            return new Command_Action
            {
                defaultLabel = "WULA_RecallLastPilot".Translate(),
                defaultDesc = "WULA_RecallLastPilotDesc".Translate(),
                icon = Props.GetRecallPilotIcon(),
                action = () =>
                {
                    RecallLastPilot();
                },
                hotKey = KeyBindingDefOf.Misc3
            };
        }
        
        // 检查驾驶员是否可用于快速登机
        private bool IsPilotAvailableForRecall(Pawn pilot)
        {
            if (pilot == null || pilot.Dead || pilot.Destroyed)
                return false;
                
            if (innerContainer.Contains(pilot))
                return false;
                
            if (!CanAddPilot(pilot))
                return false;
                
            if (parent.Map == null || !pilot.Spawned || pilot.Map != parent.Map)
                return false;
                
            if (pilot.Position.DistanceTo(parent.Position) > Props.recallRadius)
                return false;
                
            if (!pilot.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
                return false;
                
            if (pilot.IsPrisoner || pilot.IsSlave)
                return false;
                
            return true;
        }
        
        // 召回上次驾驶员
        private void RecallLastPilot()
        {
            if (lastPilot == null || IsFull || !IsPilotAvailableForRecall(lastPilot))
            {
                return;
            }
            
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMech, parent);
            lastPilot.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public CompMechPilotHolder()
        {
            innerContainer = new ThingOwner<Pawn>(this);
        }

        public void RemoveAllPilots(IntVec3? exitPos = null)
        {
            bool hadPilots = HasPilots;

            var pilotsToRemove = innerContainer.ToList();

            foreach (var thing in pilotsToRemove)
            {
                if (thing is Pawn pawn)
                {
                    UnsyncPilotHediffs(pawn);
                }
            }

            foreach (var thing in pilotsToRemove)
            {
                if (thing is Pawn pawn)
                {
                    RemovePilot(pawn, exitPos);
                }
            }

            if (hadPilots && parent is Pawn mech)
            {
                StopMechJobs();
            }
        }

        public void EjectAllPilotsOnDeath()
        {
            if (isProcessingDestruction)
                return;

            try
            {
                isProcessingDestruction = true;

                if (!HasPilots)
                {
                    return;
                }

                var pilots = innerContainer.ToList();
                foreach (var thing in pilots)
                {
                    if (thing is Pawn pawn)
                    {
                        UnsyncPilotHediffs(pawn);
                    }
                }

                IntVec3 ejectPos = FindSafeEjectPosition();

                foreach (var thing in pilots)
                {
                    if (thing is Pawn pawn)
                    {
                        innerContainer.Remove(pawn);
                        TrySpawnPilotAtPosition(pawn, ejectPos);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 弹出驾驶员时发生错误: {ex}");
            }
            finally
            {
                isProcessingDestruction = false;
            }
        }

        private IntVec3 FindSafeEjectPosition()
        {
            Map map = parent.Map;
            if (map == null)
                return parent.Position;

            IntVec3 pos = parent.Position;

            if (!pos.Walkable(map) || pos.Fogged(map))
            {
                for (int i = 1; i <= 5; i++)
                {
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(pos, i, true))
                    {
                        if (cell.Walkable(map) && !cell.Fogged(map))
                        {
                            return cell;
                        }
                    }
                }
            }

            if (!pos.Walkable(map) || pos.Fogged(map))
            {
                CellFinder.TryFindRandomCellNear(pos, map, 10,
                    cell => cell.Walkable(map) && !cell.Fogged(map),
                    out pos, 100);
            }

            return pos;
        }

        private bool TrySpawnPilotAtPosition(Pawn pawn, IntVec3 position)
        {
            Map map = parent.Map;
            if (map == null)
            {
                Log.Error($"[WULA] 尝试在没有地图的情况下生成驾驶员: {pawn.LabelShort}");
                return false;
            }

            try
            {
                if (GenGrid.InBounds(position, map) && position.Walkable(map) && !position.Fogged(map))
                {
                    GenSpawn.Spawn(pawn, position, map, WipeMode.Vanish);
                    return true;
                }

                IntVec3 spawnPos;
                if (RCellFinder.TryFindRandomCellNearWith(position,
                    cell => cell.Walkable(map) && !cell.Fogged(map),
                    map, out spawnPos, 1, 10))
                {
                    GenSpawn.Spawn(pawn, spawnPos, map, WipeMode.Vanish);
                    return true;
                }

                CellFinder.TryFindRandomCellNear(position, map, 20,
                    cell => cell.Walkable(map) && !cell.Fogged(map),
                    out spawnPos);
                GenSpawn.Spawn(pawn, spawnPos, map, WipeMode.Vanish);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 生成驾驶员时发生错误: {ex}");
                return false;
            }
        }

        public Pawn GetPrimaryPilot()
        {
            if (innerContainer.Count > 0)
            {
                foreach (var thing in innerContainer)
                {
                    if (thing is Pawn pawn)
                        return pawn;
                }
            }
            return null;
        }

        public IEnumerable<Pawn> GetPilots()
        {
            foreach (var thing in innerContainer)
            {
                if (thing is Pawn pawn)
                    yield return pawn;
            }
        }

        public void Notify_PilotAdded(Pawn pilot)
        {
            if (pilot.Faction == Faction.OfPlayer)
            {
                Messages.Message("WULA_PilotEnteredMech".Translate(pilot.LabelShort, parent.LabelShort),
                    parent, MessageTypeDefOf.PositiveEvent);
            }
        }

        public void Notify_PilotRemoved(Pawn pilot)
        {
            if (pilot.Faction == Faction.OfPlayer)
            {
                Messages.Message("WULA_PilotExitedMech".Translate(pilot.LabelShort, parent.LabelShort),
                    parent, MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (HasPilots)
            {
                EjectAllPilotsOnDeath();
            }

            base.PostDestroy(mode, previousMap);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        private void ShowPilotSelectionMenu()
        {
            if (!(parent is Wulamechunit mech))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            var allColonists = mech.Map.mapPawns.FreeColonists
                .Where(p => CanAddPilot(p))
                .ToList();

            var ableColonists = allColonists.Where(p => CanPawnMoveToMech(p, mech)).ToList();
            var disabledColonists = allColonists.Where(p => !CanPawnMoveToMech(p, mech)).ToList();

            if (ableColonists.Count == 0 && disabledColonists.Count == 0)
            {
                options.Add(new FloatMenuOption("WULA_NoAvailablePilots".Translate(), null));
            }
            else
            {
                foreach (var colonist in ableColonists)
                {
                    string colonistLabel = colonist.LabelShortCap;
                    Action action = () => OrderColonistToEnterMech(colonist);

                    FloatMenuOption option = new FloatMenuOption(
                        colonistLabel,
                        action,
                        colonist,
                        Color.white,
                        MenuOptionPriority.Default
                    );

                    options.Add(option);
                }

                foreach (var colonist in disabledColonists)
                {
                    string colonistLabel = colonist.LabelShortCap + " " + "WULA_DisabledColonistRequiresCarry".Translate();
                    Action action = () => OrderCarryDisabledColonistToMech(colonist);

                    FloatMenuOption option = new FloatMenuOption(
                        colonistLabel,
                        action,
                        colonist,
                        Color.yellow,
                        MenuOptionPriority.Default
                    );

                    options.Add(option);
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OrderColonistToEnterMech(Pawn colonist)
        {
            if (!(parent is Wulamechunit mech) || colonist == null)
                return;

            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMech, mech);
            colonist.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void OrderCarryDisabledColonistToMech(Pawn disabledColonist)
        {
            if (!(parent is Wulamechunit mech) || disabledColonist == null)
                return;

            Pawn carrier = FindClosestAvailableCarrier(disabledColonist, mech);
            
            if (carrier == null)
            {
                Messages.Message("WULA_NoAvailableCarrier".Translate(disabledColonist.LabelShortCap), 
                    parent, MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_CarryToMech, disabledColonist, mech);
            carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            
            Messages.Message("WULA_CarrierAssigned".Translate(carrier.LabelShortCap, disabledColonist.LabelShortCap), 
                parent, MessageTypeDefOf.PositiveEvent);
        }

        private Pawn FindClosestAvailableCarrier(Pawn disabledColonist, Wulamechunit mech)
        {
            if (disabledColonist.Map == null)
                return null;

            var potentialCarriers = disabledColonist.Map.mapPawns.FreeColonists
                .Where(p => p != disabledColonist && !p.Downed && 
                           p.CanReserveAndReach(disabledColonist, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, false) &&
                           p.CanReserveAndReach(mech, PathEndMode.Touch, Danger.Deadly, 1, -1, null, false))
                .ToList();

            if (potentialCarriers.Count == 0)
                return null;

            return potentialCarriers
                .OrderBy(p => p.Position.DistanceTo(disabledColonist.Position))
                .FirstOrDefault();
        }
    }
}
