// File: CompMechPilotHolder.cs (添加残疾殖民者搬运逻辑)
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

        // 新增：驾驶员图标配置
        public string summonPilotIcon = "Wula/UI/Commands/WULA_Enter_Mech";
        public string ejectPilotIcon = "Wula/UI/Commands/WULA_Exit_Mech";

        public float ejectPilotHealthPercentThreshold = 0.1f; // 默认30%血量
        public bool allowEntryBelowThreshold = false; // 血量低于阈值时是否允许进入

        // 新增：Hediff同步配置
        public bool syncPilotHediffs = true;              // 是否同步驾驶员的Hediff
        public List<string> syncedHediffDefs = null;      // 需要同步的Hediff列表（null表示全部）
        public bool autoApplyHediffOnEntry = false;       // 进入时自动添加指定的Hediff
        public HediffDef autoHediffDef = null;            // 自动添加的Hediff
        public float autoHediffSeverity = 0.5f;           // 自动添加的Hediff严重性

        public CompProperties_MechPilotHolder()
        {
            this.compClass = typeof(CompMechPilotHolder);
        }

        // 新增：加载图标的方法
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
    }

    public class CompMechPilotHolder : ThingComp, IThingHolder, ISuspendableThingHolder
    {
        public ThingOwner innerContainer;

        // 标记是否正在处理死亡/销毁事件，避免重复处理
        private bool isProcessingDestruction = false;

        // 新增：记录是否已经因为低血量弹出过驾驶员
        private bool hasEjectedDueToLowHealth = false;

        // 新增：存储驾驶员同步的Hediff
        private Dictionary<Pawn, List<Hediff>> syncedHediffs = new Dictionary<Pawn, List<Hediff>>();

        public CompProperties_MechPilotHolder Props => (CompProperties_MechPilotHolder)props;

        public int CurrentPilotCount => innerContainer.Count;
        public bool HasPilots => innerContainer.Count > 0;
        public bool HasRoom => innerContainer.Count < Props.maxPilots;
        public bool IsFull => innerContainer.Count >= Props.maxPilots;

        public bool IsContentsSuspended => true;

        // 新增：获取精神状态定义
        private MentalStateDef MechNoPilotStateDef => WULA_MentalStateDefOf.WULA_MechNoPilot;

        // 新增：检查并更新精神状态
        private void CheckAndUpdateMentalState()
        {
            var mech = parent as Pawn;
            if (mech == null || mech.Dead || MechNoPilotStateDef == null)
                return;

            // 如果没有驾驶员，尝试进入待机状态
            if (!HasPilots)
            {
                if (mech.MentalStateDef != MechNoPilotStateDef && !mech.InMentalState)
                {
                    mech.mindState.mentalStateHandler.TryStartMentalState(MechNoPilotStateDef, null, true);
                }
            }
            // 如果有驾驶员，确保退出待机状态
            else
            {
                if (mech.MentalStateDef == MechNoPilotStateDef)
                {
                    mech.mindState.mentalStateHandler.CurState?.RecoverFromState();
                }
            }
        }

        // 修改：添加驾驶员 - 添加Hediff同步功能
        public void AddPilot(Pawn pawn)
        {
            if (!CanAddPilot(pawn))
                return;

            // 将pawn添加到容器中
            if (pawn.Spawned)
                pawn.DeSpawnOrDeselect();

            innerContainer.TryAdd(pawn, true);

            // 停止pawn的移动
            pawn.pather?.StopDead();
            pawn.jobs?.StopAll();

            // 触发事件
            Notify_PilotAdded(pawn);

            // 更新机甲的精神状态
            CheckAndUpdateMentalState();

            // 新增：同步驾驶员的Hediff
            if (Props.syncPilotHediffs)
            {
                SyncPilotHediffs(pawn);
            }

            // 新增：自动添加Hediff
            if (Props.autoApplyHediffOnEntry && Props.autoHediffDef != null)
            {
                AddAutoHediff(pawn);
            }
        }

        // 修改：移除驾驶员 - 添加Hediff取消同步功能
        public void RemovePilot(Pawn pawn, IntVec3? exitPos = null)
        {
            // 新增：移除前，清理同步的Hediff
            if (Props.syncPilotHediffs)
            {
                UnsyncPilotHediffs(pawn);
            }

            if (innerContainer.Contains(pawn))
            {
                // 从容器中移除
                innerContainer.Remove(pawn);

                // 将pawn放回地图
                TrySpawnPilotAtPosition(pawn, exitPos ?? parent.Position);

                // 触发事件
                Notify_PilotRemoved(pawn);

                // 停止机甲的工作
                StopMechJobs();

                // 更新机甲的精神状态
                CheckAndUpdateMentalState();
            }
        }

        // 新增：同步驾驶员的Hediff
        private void SyncPilotHediffs(Pawn pawn)
        {
            // 修复：确保parent是Wulamechunit类型
            if (pawn == null || !(parent is Wulamechunit mech))
                return;

            try
            {
                var hediffsToSync = new List<Hediff>();

                // 收集需要同步的Hediff
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (ShouldSyncHediff(hediff))
                    {
                        hediffsToSync.Add(hediff);

                        // 激活Hediff的同步组件
                        var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                        if (syncComp != null)
                        {
                            syncComp.OnPilotEnteredMech(mech); // 这里现在应该可以了
                        }
                    }
                }

                // 存储同步的Hediff
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

        // 新增：取消同步驾驶员的Hediff
        private void UnsyncPilotHediffs(Pawn pawn)
        {
            if (pawn == null || !syncedHediffs.ContainsKey(pawn))
                return;

            try
            {
                // 通知所有同步的Hediff断开连接
                foreach (var hediff in syncedHediffs[pawn])
                {
                    var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                    if (syncComp != null)
                    {
                        syncComp.OnPilotExitedMech();
                    }
                }

                // 从记录中移除
                syncedHediffs.Remove(pawn);
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 取消同步Hediff时出错: {ex}");
            }
        }

        // 新增：判断Hediff是否需要同步
        private bool ShouldSyncHediff(Hediff hediff)
        {
            if (hediff == null)
                return false;

            // 检查是否有同步组件
            var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
            if (syncComp == null)
                return false;

            // 检查是否在指定的同步列表中
            if (Props.syncedHediffDefs != null &&
                Props.syncedHediffDefs.Count > 0)
            {
                return Props.syncedHediffDefs.Contains(hediff.def.defName);
            }

            // 默认同步所有有同步组件的Hediff
            return true;
        }

        // 新增：自动添加Hediff
        private void AddAutoHediff(Pawn pawn)
        {
            try
            {
                // 检查是否已经有相同的Hediff
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

        // 修改：在CompTick中添加Hediff同步检查
        public override void CompTick()
        {
            base.CompTick();

            try
            {
                // 每60帧检查一次血量和精神状态
                if (Find.TickManager.TicksGame % 60 == 0)
                {
                    CheckLowHealth();
                    CheckAndUpdateMentalState();
                }

                // 每120帧检查一次Hediff同步状态
                if (Find.TickManager.TicksGame % 120 == 0)
                {
                    CheckHediffSync();
                }

                // 检查机甲是否死亡
                var mech = parent as Pawn;
                if (mech != null && mech.Dead && HasPilots)
                {
                    EjectAllPilotsOnDeath();
                    return;
                }

                // 定期检查驾驶员状态
                var pilotsToRemove = new List<Pawn>();
                foreach (var thing in innerContainer)
                {
                    if (thing is Pawn pawn && (pawn.Dead))
                    {
                        pilotsToRemove.Add(pawn);
                    }
                }

                foreach (var pawn in pilotsToRemove)
                {
                    RemovePilot(pawn);
                }

                // 确保容器内的pawn处于正确状态
                foreach (var thing in innerContainer)
                {
                    if (thing is Pawn pawn)
                    {
                        // 确保pawn在容器内不执行任何工作
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

        // 新增：检查Hediff同步状态
        private void CheckHediffSync()
        {
            // 修复：确保parent是Wulamechunit类型
            if (!Props.syncPilotHediffs || !(parent is Wulamechunit))
                return;

            try
            {
                // 检查每个驾驶员的同步状态
                foreach (var pilot in GetPilots())
                {
                    if (pilot == null || pilot.Dead || pilot.Destroyed)
                        continue;

                    // 检查是否有新的需要同步的Hediff
                    SyncPilotHediffs(pilot);

                    // 检查是否有需要移除的Hediff
                    if (syncedHediffs.ContainsKey(pilot))
                    {
                        var currentHediffs = pilot.health.hediffSet.hediffs
                            .Where(ShouldSyncHediff)
                            .ToList();

                        // 找出不再存在的Hediff
                        var removedHediffs = syncedHediffs[pilot]
                            .Where(h => !currentHediffs.Contains(h))
                            .ToList();

                        // 清理不再存在的Hediff
                        foreach (var hediff in removedHediffs)
                        {
                            var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                            if (syncComp != null)
                            {
                                syncComp.OnPilotExitedMech();
                            }
                        }

                        // 更新记录
                        syncedHediffs[pilot] = currentHediffs;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 检查Hediff同步状态时出错: {ex}");
            }
        }

        // 修改：在生成后初始化精神状态
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!(parent is Wulamechunit))
            {
                Log.Warning($"[WULA] CompMechPilotHolder attached to non-mech: {parent}");
            }

            // 确保加载后恢复状态
            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Pawn>(this);
            }

            // 初始化精神状态
            CheckAndUpdateMentalState();

            // 新增：加载后重新同步Hediff
            if (Props.syncPilotHediffs)
            {
                foreach (var pilot in GetPilots())
                {
                    SyncPilotHediffs(pilot);
                }
            }
        }

        // 修改：在数据保存和加载时处理Hediff同步
        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref isProcessingDestruction, "isProcessingDestruction", false);
            Scribe_Values.Look(ref hasEjectedDueToLowHealth, "hasEjectedDueToLowHealth", false);
            Scribe_Collections.Look(ref syncedHediffs, "syncedHediffs",
                LookMode.Reference, LookMode.Deep);

            // 加载后检查精神状态和Hediff同步
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CheckAndUpdateMentalState();
                
                if (Props.syncPilotHediffs)
                {
                    // 重新同步所有驾驶员的Hediff
                    foreach (var pilot in GetPilots())
                    {
                        SyncPilotHediffs(pilot);
                    }
                }
            }
        }

        // 新增：停止机甲所有工作
        private void StopMechJobs()
        {
            var mech = parent as Pawn;
            if (mech == null)
                return;

            // 停止所有工作
            mech.jobs?.StopAll();

            // 停止移动
            mech.pather?.StopDead();

            // 取消征召
            var drafter = mech.drafter;
            if (drafter != null && mech.Drafted)
            {
                mech.drafter.Drafted = false;
            }

            // 停止当前所有工作队列
            mech.jobs?.ClearQueuedJobs();

            // 清除敌人目标
            mech.mindState.enemyTarget = null;
        }

        // 获取机甲当前血量百分比
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

        // 检查机甲是否低于血量阈值
        public bool IsBelowHealthThreshold
        {
            get
            {
                return CurrentHealthPercent < Props.ejectPilotHealthPercentThreshold;
            }
        }

        // 修改 CanAddPilot 方法，添加血量检查
        public bool CanAddPilot(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;
            
            // 允许无法行动但还活着的殖民者
            if (pawn.Downed)
                return true; // 这是新增的关键修改
            
            if (!HasRoom)
                return false;
            if (innerContainer.Contains(pawn))
                return false;
            // 检查工作标签
            if (!string.IsNullOrEmpty(Props.pilotWorkTag))
            {
                WorkTags tag;
                if (System.Enum.TryParse(Props.pilotWorkTag, out tag))
                {
                    if (pawn.WorkTagIsDisabled(tag))
                        return false;
                }
            }

            // 新增：检查血量阈值
            if (!Props.allowEntryBelowThreshold && IsBelowHealthThreshold)
            {
                return false;
            }
            return true;
        }

        // 修改：检查殖民者是否能够自行移动到机甲
        private bool CanPawnMoveToMech(Pawn pawn, Wulamechunit mech)
        {
            if (pawn == null || mech == null)
                return false;
            
            // 如果殖民者无法行动，需要搬运
            if (pawn.Downed)
                return false;
            
            // 检查殖民者是否能到达机甲
            return pawn.CanReach(mech, PathEndMode.Touch, Danger.Deadly);
        }

        // 修改 CompMechPilotHolder 的 CheckLowHealth 方法
        private void CheckLowHealth()
        {
            if (IsBelowHealthThreshold && HasPilots)
            {
                // 如果低于阈值且有驾驶员，弹出所有驾驶员
                EjectPilotsDueToLowHealth();
            }
            else if (!IsBelowHealthThreshold)
            {
                // 如果恢复到阈值以上，重置标记
                hasEjectedDueToLowHealth = false;
            }
        }

        // 新增：因为低血量弹出驾驶员
        private void EjectPilotsDueToLowHealth()
        {
            if (hasEjectedDueToLowHealth)
                return;

            // 弹出所有驾驶员
            RemoveAllPilots();

            // 发送消息
            if (parent.Faction == Faction.OfPlayer)
            {
                Messages.Message("WULA_PilotsEjectedDueToLowHealth".Translate(parent.LabelShort,
                    (Props.ejectPilotHealthPercentThreshold * 100).ToString("F0")),
                    parent, MessageTypeDefOf.NegativeEvent);
            }

            hasEjectedDueToLowHealth = true;
        }

        // 新增：在承受伤害后检查血量
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);

            // 如果机甲死亡，弹出驾驶员
            var mech = parent as Pawn;
            if (mech != null && mech.Dead)
            {
                EjectAllPilotsOnDeath();
            }
            else
            {
                // 检查是否因为伤害导致血量过低
                CheckLowHealth();
            }
        }

        // 修改 Gizmo 显示，添加血量信息和Hediff同步状态
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 修复：确保parent是Wulamechunit类型
            if (!(parent is Wulamechunit mech) || mech.Faction != Faction.OfPlayer)
                yield break;

            // 召唤驾驶员Gizmo
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

                // 如果血量低于阈值且不允许进入，禁用按钮
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

        public CompMechPilotHolder()
        {
            innerContainer = new ThingOwner<Pawn>(this);
        }

        // 修改：弹出所有驾驶员时取消Hediff同步
        public void RemoveAllPilots(IntVec3? exitPos = null)
        {
            // 记录是否有驾驶员
            bool hadPilots = HasPilots;

            // 复制列表以避免迭代时修改的问题
            var pilotsToRemove = innerContainer.ToList();

            // 先取消所有Hediff同步
            foreach (var thing in pilotsToRemove)
            {
                if (thing is Pawn pawn)
                {
                    UnsyncPilotHediffs(pawn);
                }
            }

            // 然后移除所有驾驶员
            foreach (var thing in pilotsToRemove)
            {
                if (thing is Pawn pawn)
                {
                    RemovePilot(pawn, exitPos);
                }
            }

            // 如果有机甲并且原来有驾驶员，现在没有了，停止工作
            if (hadPilots && parent is Pawn mech)
            {
                StopMechJobs();
            }
        }

        // 修改：专门用于死亡/销毁时弹出驾驶员的方法，取消Hediff同步
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

                // 先取消所有Hediff同步
                var pilots = innerContainer.ToList();
                foreach (var thing in pilots)
                {
                    if (thing is Pawn pawn)
                    {
                        UnsyncPilotHediffs(pawn);
                    }
                }

                // 获取安全位置
                IntVec3 ejectPos = FindSafeEjectPosition();

                // 弹出所有驾驶员
                foreach (var thing in pilots)
                {
                    if (thing is Pawn pawn)
                    {
                        // 从容器中移除
                        innerContainer.Remove(pawn);

                        // 尝试生成到地图上
                        if (TrySpawnPilotAtPosition(pawn, ejectPos))
                        {
                            // 驾驶员成功弹出
                        }
                        else
                        {
                            Log.Error($"[WULA] 无法弹出驾驶员: {pawn.LabelShort}");
                        }
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

            // 优先选择机甲周围的安全位置
            IntVec3 pos = parent.Position;

            // 如果当前位置不安全，查找周围安全位置
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

            // 如果周围没有安全位置，使用随机位置
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

            // 尝试在指定位置生成
            try
            {
                if (GenGrid.InBounds(position, map) && position.Walkable(map) && !position.Fogged(map))
                {
                    GenSpawn.Spawn(pawn, position, map, WipeMode.Vanish);
                    return true;
                }

                // 如果指定位置不行，找附近的位置
                IntVec3 spawnPos;
                if (RCellFinder.TryFindRandomCellNearWith(position,
                    cell => cell.Walkable(map) && !cell.Fogged(map),
                    map, out spawnPos, 1, 10))
                {
                    GenSpawn.Spawn(pawn, spawnPos, map, WipeMode.Vanish);
                    return true;
                }

                // 实在找不到位置，就在任意位置生成
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

        // 关键修复：重写销毁相关方法
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // 先弹出所有驾驶员并取消Hediff同步
            if (HasPilots)
            {
                EjectAllPilotsOnDeath();
            }

            base.PostDestroy(mode, previousMap);
        }

        // IThingHolder 接口实现
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        // 修改：显示驾驶员选择菜单，包含无法行动的殖民者
        private void ShowPilotSelectionMenu()
        {
            // 修复：确保parent是Wulamechunit类型
            if (!(parent is Wulamechunit mech))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // 获取所有可用的殖民者（包括无法行动的）
            var allColonists = mech.Map.mapPawns.FreeColonists
                .Where(p => CanAddPilot(p))
                .ToList();

            // 分类：能够行动和无法行动的
            var ableColonists = allColonists.Where(p => CanPawnMoveToMech(p, mech)).ToList();
            var disabledColonists = allColonists.Where(p => !CanPawnMoveToMech(p, mech)).ToList();

            // 为能够行动的殖民者创建选项
            if (ableColonists.Count == 0 && disabledColonists.Count == 0)
            {
                options.Add(new FloatMenuOption("WULA_NoAvailablePilots".Translate(), null));
            }
            else
            {
                // 能够行动的殖民者：直接进入
                foreach (var colonist in ableColonists)
                {
                    string colonistLabel = colonist.LabelShortCap;
                    Action action = () => OrderColonistToEnterMech(colonist);

                    FloatMenuOption option = new FloatMenuOption(
                        colonistLabel,
                        action,
                        colonist,
                        Color.white,
                        MenuOptionPriority.Default,
                        null,
                        null,
                        0f,
                        null,
                        null,
                        true,
                        0
                    );

                    options.Add(option);
                }

                // 无法行动的殖民者：需要搬运
                foreach (var colonist in disabledColonists)
                {
                    string colonistLabel = colonist.LabelShortCap + " " + "WULA_DisabledColonistRequiresCarry".Translate();
                    Action action = () => OrderCarryDisabledColonistToMech(colonist);

                    FloatMenuOption option = new FloatMenuOption(
                        colonistLabel,
                        action,
                        colonist,
                        Color.yellow,
                        MenuOptionPriority.Default,
                        null,
                        null,
                        0f,
                        null,
                        null,
                        true,
                        0
                    );

                    options.Add(option);
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OrderColonistToEnterMech(Pawn colonist)
        {
            // 修复：确保parent是Wulamechunit类型
            if (!(parent is Wulamechunit mech) || colonist == null)
                return;

            // 为殖民者安排进入机甲的工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMech, mech);
            colonist.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        // 新增：为残疾殖民者安排搬运工作
        private void OrderCarryDisabledColonistToMech(Pawn disabledColonist)
        {
            if (!(parent is Wulamechunit mech) || disabledColonist == null)
                return;

            // 寻找最近的、能够搬运的殖民者
            Pawn carrier = FindClosestAvailableCarrier(disabledColonist, mech);
            
            if (carrier == null)
            {
                Messages.Message("WULA_NoAvailableCarrier".Translate(disabledColonist.LabelShortCap), 
                    parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 为搬运者安排搬运工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_CarryToMech, disabledColonist, mech);
            carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            
            Messages.Message("WULA_CarrierAssigned".Translate(carrier.LabelShortCap, disabledColonist.LabelShortCap), 
                parent, MessageTypeDefOf.PositiveEvent);
        }

        // 新增：寻找最近的可用搬运者
        private Pawn FindClosestAvailableCarrier(Pawn disabledColonist, Wulamechunit mech)
        {
            if (disabledColonist.Map == null)
                return null;

            // 寻找能够行动的殖民者，并且能够搬运
            var potentialCarriers = disabledColonist.Map.mapPawns.FreeColonists
                .Where(p => p != disabledColonist && !p.Downed && 
                           p.CanReserveAndReach(disabledColonist, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, false) &&
                           p.CanReserveAndReach(mech, PathEndMode.Touch, Danger.Deadly, 1, -1, null, false))
                .ToList();

            if (potentialCarriers.Count == 0)
                return null;

            // 选择最近的殖民者
            return potentialCarriers
                .OrderBy(p => p.Position.DistanceTo(disabledColonist.Position))
                .FirstOrDefault();
        }
    }
}
