using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 内置空间武装穿梭机 - 基于原版MapPortal机制的口袋空间实现
    /// 结合了武装防御能力和口袋空间技术的复合型载具
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_ArmedShuttleWithPocket : Building_ArmedShuttle, IThingHolder
    {
        #region 静态图标定义（使用原版MapPortal的图标）


        /// <summary>取消进入图标</summary>
        private static readonly Texture2D CancelEnterTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        /// <summary>默认进入图标</summary>
        private static readonly Texture2D DefaultEnterTex = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");

        #endregion
        #region 口袋空间字段

        /// <summary>内部口袋地图实例</summary>
        private Map pocketMap;

        /// <summary>口袋地图是否已生成</summary>
        private bool pocketMapGenerated;

        /// <summary>内部空间大小</summary>
        private IntVec2 pocketMapSize = new IntVec2(80, 80);

        /// <summary>地图生成器定义</summary>
        private MapGeneratorDef mapGenerator;

        /// <summary>退出点定义</summary>
        private ThingDef exitDef;

        /// <summary>允许直接访问（无需骇入）</summary>
        private bool allowDirectAccess = true;

        /// <summary>传送功能是否暂停（飞行时为 true）</summary>
        private bool transportDisabled = false;

        // 注意：我们不再使用自定义的innerContainer，
        // 所有物品都存储在CompTransporter.innerContainer中，保持简单和一致

        /// <summary>新的口袋空间物品容器</summary>
        private PocketSpaceThingHolder pocketSpaceContainer;

        /// <summary>口袋地图退出点（模仿原版 MapPortal.exit）</summary>
        public Building_PocketMapExit exit;

        /// <summary>是否已经进入过（模仿原版 MapPortal.beenEntered）</summary>
        protected bool beenEntered;

        /// <summary>待加载物品列表（模仿原版 MapPortal.leftToLoad）</summary>
        public List<TransferableOneWay> leftToLoad;

        /// <summary>是否已通知无法加载更多（模仿原版 MapPortal.notifiedCantLoadMore）</summary>
        public bool notifiedCantLoadMore;

        #endregion

        #region 属性

        /// <summary>获取内部口袋地图</summary>
        public Map PocketMap => pocketMap;

        /// <summary>口袋地图是否已生成</summary>
        public bool PocketMapGenerated => pocketMapGenerated;

        /// <summary>是否允许直接访问口袋空间</summary>
        public bool AllowDirectAccess => allowDirectAccess;

        // 注意：我们不再提供InnerContainer属性，因为所有物品都在CompTransporter.innerContainer中

        /// <summary>
        /// 获取进入按钮的图标
        /// </summary>
        protected virtual Texture2D EnterTex => DefaultEnterTex;

        /// <summary>
        /// 获取进入按钮的文本
        /// </summary>
        public virtual string EnterString => "WULA.PocketSpace.Enter".Translate();

        /// <summary>
        /// 获取取消进入按钮的文本
        /// </summary>
        public virtual string CancelEnterString => "WULA.PocketSpace.CancelEnter".Translate();

        /// <summary>
        /// 获取进入中的文本
        /// </summary>
        public virtual string EnteringString => "WULA.PocketSpace.Entering".Translate();

        /// <summary>加载是否正在进行（模仿原版 MapPortal.LoadInProgress）</summary>
        public bool LoadInProgress
        {
            get
            {
                if (leftToLoad != null)
                {
                    return leftToLoad.Any();
                }
                return false;
            }
        }

        /// <summary>是否有Pawn可以加载任何东西（模仿原版 MapPortal.AnyPawnCanLoadAnythingNow）</summary>
        public bool AnyPawnCanLoadAnythingNow
        {
            get
            {
                if (!LoadInProgress)
                {
                    return false;
                }
                if (!Spawned)
                {
                    return false;
                }
                // 简化版本，只检查基本条件
                return Map.mapPawns.AllPawnsSpawned.Any(p => p.IsColonist && p.CanReach(this, PathEndMode.Touch, Danger.Deadly));
            }
        }

        #endregion

        #region IThingHolder 实现 (模仿 MapPortal)

        /// <summary>
        /// 获取直接持有的物品（模仿 MapPortal.GetDirectlyHeldThings）
        /// </summary>
        public ThingOwner GetDirectlyHeldThings()
        {
            return pocketSpaceContainer.innerContainer;
        }

        /// <summary>
        /// 获取子持有者（模仿 MapPortal.GetChildHolders）
        /// </summary>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            // 目前没有子持有者，留空
        }


        /// <summary>
        /// 实现IThingHolder.ParentHolder属性
        /// </summary>
        public new IThingHolder ParentHolder => this; 

        #endregion

        #region 构造函数

        public Building_ArmedShuttleWithPocket()
        {
            Log.Message("[WULA-DEBUG] Building_ArmedShuttleWithPocket constructor called");
            pocketSpaceContainer = new PocketSpaceThingHolder(this);
        }

        #endregion

        #region 基础重写方法



        public override void PostMake()
        {
            Log.Message("[WULA-DEBUG] PostMake called");
            base.PostMake();
            // 不再初始化innerContainer，只使用CompTransporter的容器
        }

        public override void ExposeData()
        {
            Log.Message($"[WULA-DEBUG] ExposeData called, mode: {Scribe.mode}");

            base.ExposeData();
            Scribe_Deep.Look(ref pocketMap, "pocketMap");
            Scribe_Values.Look(ref pocketMapGenerated, "pocketMapGenerated", false);
            Scribe_Values.Look(ref pocketMapSize, "pocketMapSize", new IntVec2(80, 80));
            Scribe_Defs.Look(ref mapGenerator, "mapGenerator");
            Scribe_Defs.Look(ref exitDef, "exitDef");
            Scribe_Values.Look(ref allowDirectAccess, "allowDirectAccess", true);
            Scribe_Values.Look(ref transportDisabled, "transportDisabled", false);
            Scribe_Deep.Look(ref pocketSpaceContainer, "pocketSpaceContainer", this);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Log.Message("[WULA-DEBUG] PostLoadInit: Validating components after load");

                // 验证CompTransporter组件是否正常
                CompTransporter transporter = this.GetComp<CompTransporter>();
                if (transporter == null)
                {
                    Log.Error("[WULA-ERROR] CompTransporter is missing after load! This will cause item storage issues.");
                }
                else
                {
                    Log.Message($"[WULA-DEBUG] CompTransporter loaded successfully with {transporter.innerContainer?.Count ?? 0} items");
                }
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Log.Message($"[WULA-DEBUG] DeSpawn called with mode: {mode}");

            // 只在真正销毁时清理口袋地图，发射时保留
            if (ShouldDestroyPocketMapOnDeSpawn(mode))
            {
                if (pocketMap != null && pocketMapGenerated)
                {
                    try
                    {
                        Log.Message("[WULA-DEBUG] Destroying pocket map due to shuttle destruction");

                        // 将口袋空间中的物品和人员转移到主地图
                        TransferAllFromPocketToMainMap();

                        // 销毁口袋地图
                        PocketMapUtility.DestroyPocketMap(pocketMap);
                        pocketMap = null;
                        pocketMapGenerated = false;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[WULA-ERROR] Error cleaning up pocket map: {ex}");
                    }
                }
            }
            else
            {
                Log.Message("[WULA-DEBUG] Preserving pocket map during shuttle launch/transport");
                transportDisabled = true;
                if (pocketMap != null && exit != null)
                {
                    Log.Message("[WULA-DEBUG] Transport functionality disabled during flight");
                }
            }

            base.DeSpawn(mode);
        }

        /// <summary>
        /// 判断是否应该在DeSpawn时销毁口袋地图
        /// </summary>
        private bool ShouldDestroyPocketMapOnDeSpawn(DestroyMode mode)
        {
            switch (mode)
            {
                case DestroyMode.Vanish:
                    return false;
                case DestroyMode.Deconstruct:
                    return true;
                case DestroyMode.KillFinalize:
                    return true;
                case DestroyMode.Cancel:
                    return true;
                case DestroyMode.Refund:
                    return true;
                case DestroyMode.FailConstruction:
                    return true;
                default:
                    Log.Warning($"[WULA-WARNING] Unknown DestroyMode: {mode}, defaulting to preserve pocket map");
                    return false;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());

            if (pocketMapGenerated)
            {
                sb.AppendLine("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.Ready".Translate());

                CompTransporter transporter = this.GetComp<CompTransporter>();
                int mainContainerItems = transporter?.innerContainer?.Count ?? 0;

                if (mainContainerItems > 0)
                {
                    sb.AppendLine($"容器物品: {mainContainerItems}");
                }

                if (pocketMap != null)
                {
                    int pocketItems = pocketMap.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Item && t.def.EverHaulable);
                    int pawnCount = pocketMap.mapPawns.AllPawnsSpawned.Where(p => p.IsColonist).Count();

                    if (pocketItems > 0)
                    {
                        sb.AppendLine($"口袋空间物品: {pocketItems}");
                    }
                    if (pawnCount > 0)
                    {
                        sb.AppendLine("WULA.PocketSpace.PawnCount".Translate(pawnCount));
                    }
                }

                if (Prefs.DevMode)
                {
                    sb.AppendLine($"[Debug] {GetPocketSpaceDebugInfo()}");
                }
            }
            else
            {
                sb.AppendLine("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.NotGenerated".Translate());
            }

            return sb.ToString().TrimEndNewlines();
        }

        #endregion

        #region 口袋空间核心方法

        /// <summary>
        /// 检查是否可以进入口袋空间
        /// </summary>
        public bool CanEnterPocketSpace()
        {
            if (!allowDirectAccess)
            {
                return false;
            }

            if (!Spawned)
            {
                return false;
            }

            if (transportDisabled)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 进入口袋空间 - 基于原版PocketMapUtility实现
        /// </summary>
        public void EnterPocketSpace(IEnumerable<Pawn> pawns = null)
        {
            if (!CanEnterPocketSpace())
            {
                Messages.Message("WULA.PocketSpace.CannotEnter".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }

            // 创建或获取口袋地图
            if (pocketMap == null && !pocketMapGenerated)
            {
                CreatePocketMap();
            }

            if (pocketMap == null)
            {
                Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }

            // 传送玩家到口袋空间
            List<Pawn> pawnsToTransfer = new List<Pawn>();

            if (pawns != null)
            {
                pawnsToTransfer.AddRange(pawns.Where(p => p != null && p.Spawned && p.IsColonist));
            }
            else
            {
                // 如果没有指定人员，传送选中的殖民者
                pawnsToTransfer.AddRange(Find.Selector.SelectedPawns.Where(p => p.IsColonist));
            }

            if (pawnsToTransfer.Count == 0)
            {
                Messages.Message("WULA.PocketSpace.NoPawnsSelected".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }

            // 执行传送
            int transferredCount = 0;
            foreach (Pawn pawn in pawnsToTransfer)
            {
                if (pawn.Spawned)
                {
                    pawn.DeSpawn();
                }
                if (pocketSpaceContainer.innerContainer.TryAdd(pawn))
                {
                    transferredCount++;
                }
                else
                {
                    Log.Warning($"[WULA-WARNING] Failed to add pawn {pawn.LabelShort} to pocketSpaceContainer.");
                }
            }

            if (transferredCount > 0)
            {
                Messages.Message("WULA.PocketSpace.TransferSuccess".Translate(transferredCount), MessageTypeDefOf.PositiveEvent);

                // 切换到口袋地图
                Current.Game.CurrentMap = pocketMap;
                Find.CameraDriver.JumpToCurrentMapLoc(pocketMap.Center);
            }
        }

        /// <summary>
        /// 切换到口袋空间视角
        /// </summary>
        public void SwitchToPocketSpace()
        {
            if (pocketMap == null)
            {
                if (!pocketMapGenerated)
                {
                    CreatePocketMap();
                }

                if (pocketMap == null)
                {
                    Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            Current.Game.CurrentMap = pocketMap;
            Find.CameraDriver.JumpToCurrentMapLoc(pocketMap.Center);
        }

        /// <summary>
        /// 创建口袋地图 - 使用原版PocketMapUtility（模仿 MapPortal.GeneratePocketMap）
        /// </summary>
        private void CreatePocketMap()
        {
            try
            {
                // 模仿原版 MapPortal.GeneratePocketMap 的实现
                PocketMapUtility.currentlyGeneratingPortal = null; // 我们不是 MapPortal，但可以设为 null
                pocketMap = GeneratePocketMapInt();
                PocketMapUtility.currentlyGeneratingPortal = null;

                if (pocketMap != null)
                {
                    pocketMapGenerated = true;

                    // 在口袋地图中心放置退出点
                    CreateExitPoint();

                    Log.Message($"[WULA] Successfully created pocket map of size {pocketMapSize} for armed shuttle");
                }
                else
                {
                    Log.Error("[WULA] Failed to create pocket map");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Exception creating pocket map: {ex}");
            }
        }

        /// <summary>
        /// 生成口袋地图的内部实现（模仿 MapPortal.GeneratePocketMapInt）
        /// </summary>
        protected virtual Map GeneratePocketMapInt()
        {
            return PocketMapUtility.GeneratePocketMap(new IntVec3(pocketMapSize.x, 1, pocketMapSize.z), mapGenerator, GetExtraGenSteps(), this.Map);
        }

        /// <summary>
        /// 获取额外的生成步骤（模仿 MapPortal.GetExtraGenSteps）
        /// </summary>
        protected virtual IEnumerable<GenStepWithParams> GetExtraGenSteps()
        {
            return System.Linq.Enumerable.Empty<GenStepWithParams>();
        }

        /// <summary>
        /// 在口袋地图中创建退出点（模仿原版）
        /// </summary>
        private void CreateExitPoint()
        {
            if (pocketMap == null || exitDef == null) return;

            try
            {
                // 在地图中心找一个合适的位置
                IntVec3 exitPos = CellFinder.RandomClosewalkCellNear(pocketMap.Center, pocketMap, 5, (IntVec3 c) => c.IsValid && c.Standable(pocketMap) && !c.Roofed(pocketMap));

                if (exitPos.IsValid)
                {
                    exit = (Building_PocketMapExit)ThingMaker.MakeThing(exitDef);
                    GenPlace.TryPlaceThing(exit, exitPos, pocketMap, ThingPlaceMode.Direct);
                    Log.Message($"[WULA] Created exit point at {exitPos} in pocket map.");
                }
                else
                {
                    Log.Warning("[WULA] Could not find valid position for exit point in pocket map");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error creating exit point: {ex}");
            }
        }

        /// <summary>
        /// 将单个Pawn传送到口袋空间
        /// </summary>
        private bool TransferPawnToPocketSpace(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pocketMap == null) return false;

            try
            {
                // 找一个安全的位置
                IntVec3 spawnPos = CellFinder.RandomClosewalkCellNear(pocketMap.Center, pocketMap, 10,
                    p => p.Standable(pocketMap) && !p.GetThingList(pocketMap).Any(t => t is Pawn));

                if (spawnPos.IsValid)
                {
                    pawn.DeSpawn();
                    GenPlace.TryPlaceThing(pawn, spawnPos, pocketMap, ThingPlaceMode.Near);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error transferring pawn {pawn?.LabelShort} to pocket space: {ex}");
            }

            return false;
        }

        /// <summary>
        /// 将所有物品和人员从口袋空间转移到主地图
        /// </summary>
        private void TransferAllFromPocketToMainMap()
        {
            Log.Message("[WULA-DEBUG] TransferAllFromPocketToMainMap started");

            if (pocketMap == null)
            {
                Log.Warning("[WULA-DEBUG] TransferAllFromPocketToMainMap: pocketMap is null, nothing to transfer");
                return;
            }

            if (!Spawned)
            {
                Log.Error("[WULA-ERROR] TransferAllFromPocketToMainMap: Shuttle not spawned, cannot transfer items");
                return;
            }

            try
            {
                // 转移所有殖民者到 pocketSpaceContainer
                List<Pawn> pawnsToTransfer = pocketMap.mapPawns.AllPawnsSpawned.ToList();
                Log.Message($"[WULA-DEBUG] Found {pawnsToTransfer.Count} pawns to transfer from pocket map.");
                foreach (Pawn pawn in pawnsToTransfer)
                {
                    if (pawn.Spawned)
                    {
                        pawn.DeSpawn();
                    }
                    pocketSpaceContainer.innerContainer.TryAdd(pawn);
                }

                // 转移所有物品到 pocketSpaceContainer
                List<Thing> itemsToTransfer = pocketMap.listerThings.AllThings.Where(t => t.def.category == ThingCategory.Item && t.def.EverHaulable).ToList();
                Log.Message($"[WULA-DEBUG] Found {itemsToTransfer.Count} items to transfer from pocket map.");
                foreach (Thing item in itemsToTransfer)
                {
                    if (item.Spawned)
                    {
                        item.DeSpawn();
                    }
                    pocketSpaceContainer.innerContainer.TryAdd(item);
                }

                Log.Message($"[WULA] Transferred all pawns and items from pocket map to pocketSpaceContainer.");

                // 调用新的同步方法，将 pocketSpaceContainer 中的所有物品和 Pawn 转移到主地图的 CompTransporter
                TransferPocketContainerToMainTransporter();
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA-ERROR] Error transferring from pocket map: {ex}");
                Log.Error($"[WULA-ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 将pocketSpaceContainer中的所有物品和Pawn转移到主地图的CompTransporter
        /// </summary>
        public void TransferPocketContainerToMainTransporter()
        {
            Log.Message("[WULA-DEBUG] TransferPocketContainerToMainTransporter started.");

            CompTransporter transporter = this.GetComp<CompTransporter>();
            if (transporter == null)
            {
                Log.Error("[WULA-ERROR] CompTransporter not found on shuttle! Cannot transfer items from pocketSpaceContainer.");
                return;
            }

            List<Thing> thingsToTransfer = pocketSpaceContainer.innerContainer.ToList();
            int transferredCount = 0;

            foreach (Thing t in thingsToTransfer)
            {
                if (pocketSpaceContainer.innerContainer.Remove(t))
                {
                    if (transporter.innerContainer.TryAdd(t))
                    {
                        transferredCount++;
                    }
                    else
                    {
                        Log.Warning($"[WULA-WARNING] Failed to add {t.LabelShort} to main transporter container. Dropping on ground.");
                        GenPlace.TryPlaceThing(t, this.Position, this.Map, ThingPlaceMode.Near);
                    }
                }
            }
            Log.Message($"[WULA] Transferred {transferredCount} items/pawns from pocketSpaceContainer to main transporter.");
        }

        /// <summary>
        /// 获取口袋空间状态信息（用于调试）
        /// </summary>
        public string GetPocketSpaceDebugInfo()
        {
            if (!pocketMapGenerated || pocketMap == null)
            {
                return "Pocket space not initialized";
            }

            CompTransporter transporter = this.GetComp<CompTransporter>();
            int pocketItems = pocketMap.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Item && t.def.EverHaulable);
            int mainContainerItems = transporter?.innerContainer?.Count ?? 0;

            return $"Pocket: {pocketItems}, Main: {mainContainerItems}";
        }

        #endregion

        #region Gizmo方法

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (allowDirectAccess)
            {
                // 进入口袋空间按钮（模仿原版MapPortal）
                Command_Action enterCommand = new Command_Action();
                enterCommand.action = delegate
                {
                    // 使用自定义的殖民者选择对话框，模仿原版Dialog_EnterPortal的行为
                    OpenPawnSelectionDialog();
                };
                enterCommand.icon = EnterTex;
                enterCommand.defaultLabel = EnterString + "...";
                enterCommand.defaultDesc = "WULA.PocketSpace.EnterDesc".Translate();

                // 检查是否可以进入（模仿原版MapPortal.IsEnterable）
                string reason;
                enterCommand.Disabled = !IsEnterable(out reason);
                enterCommand.disabledReason = reason;
                yield return enterCommand;


            }
        }



        #endregion

        #region MapPortal兼容接口（使Dialog_EnterPortal能正常工作）

        /// <summary>
        /// 判断是否可以进入（模仿原版MapPortal.IsEnterable）
        /// </summary>
        public virtual bool IsEnterable(out string reason)
        {
            reason = "";
            if (!Spawned)
            {
                reason = "WULA.PocketSpace.NotSpawned".Translate();
                return false;
            }
            if (transportDisabled)
            {
                reason = "WULA.PocketSpace.TransportDisabled".Translate();
                return false;
            }
            if (!this.CanEnterPocketSpace())
            {
                reason = "WULA.PocketSpace.CannotEnterReason".Translate();
                return false;
            }
            return true;
        }

        /// <summary>
        /// 获取另一个地图（模仿原版MapPortal.GetOtherMap）
        /// </summary>
        public virtual Map GetOtherMap()
        {
            if (PocketMap == null)
            {
                CreatePocketMap();
            }
            return PocketMap;
        }

        /// <summary>
        /// 获取目标位置（模仿原版MapPortal.GetDestinationLocation）
        /// </summary>
        public virtual IntVec3 GetDestinationLocation()
        {
            return exit?.Position ?? IntVec3.Invalid;
        }

        /// <summary>
        /// 进入时回调（模仿原版MapPortal.OnEntered）
        /// </summary>
        public virtual void OnEntered(Pawn pawn)
        {
            // 将Pawn添加到口袋空间容器
            if (pawn.Spawned)
            {
                pawn.DeSpawn();
            }
            pocketSpaceContainer.innerContainer.TryAdd(pawn);

            if (!beenEntered)
            {
                beenEntered = true;
                // 这里可以添加一些首次进入的信件/事件
            }
            if (Find.CurrentMap == base.Map)
            {
                // def.portal.traverseSound?.PlayOneShot(this); // 暂时移除，避免NRE
            }
            else if (Find.CurrentMap == exit.Map)
            {
                // def.portal.traverseSound?.PlayOneShot(exit); // 暂时移除，避免NRE
            }
        }

        /// <summary>
        /// 打开殖民者选择对话框（模仿原版Dialog_EnterPortal）
        /// </summary>
        private void OpenPawnSelectionDialog()
        {
            List<Pawn> pawns = CaravanFormingUtility.AllSendablePawns(this.Map, true, true, true, true, true, 0).ToList();
            List<Thing> items = CaravanFormingUtility.AllReachableColonyItems(this.Map, true, true).ToList();

            // 创建并显示对话框
            Dialog_EnterPortal window = new Dialog_EnterPortal(new global::WulaFallenEmpire.MapPortalAdapter(this)); // 使用适配器
            Find.WindowStack.Add(window);
        }

        /// <summary>
        /// 通知物品被添加到此持有者（从IThingHolder继承，但现在由PocketSpaceThingHolder处理）
        /// </summary>
        public void Notify_ThingAdded(Thing t)
        {
            // 这个方法现在由 PocketSpaceThingHolder 内部处理，这里只是为了满足IThingHolder接口
            // 或者，如果Building_ArmedShuttleWithPocket仍然需要实现IThingHolder，则可以将其转发
            // Log.Message($"[WULA] Building_ArmedShuttleWithPocket.Notify_ThingAdded called for {t.LabelCap}");
            // pocketSpaceContainer.innerContainer.Notify_ThingAdded(t); // 转发给内部容器
        }

        /// <summary>
        /// 添加到待加载列表（模仿原版MapPortal.AddToTheToLoadList）
        /// </summary>
        public void AddToTheToLoadList(TransferableOneWay t, int count)
        {
            if (!t.HasAnyThing || count <= 0)
            {
                return;
            }
            if (leftToLoad == null)
            {
                leftToLoad = new List<TransferableOneWay>();
            }
            TransferableOneWay transferableOneWay = TransferableUtility.TransferableMatching(t.AnyThing, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferableOneWay != null)
            {
                for (int i = 0; i < t.things.Count; i++)
                {
                    if (!transferableOneWay.things.Contains(t.things[i]))
                    {
                        transferableOneWay.things.Add(t.things[i]);
                    }
                }
                if (transferableOneWay.CanAdjustBy(count).Accepted)
                {
                    transferableOneWay.AdjustBy(count);
                }
            }
            else
            {
                TransferableOneWay transferableOneWay2 = new TransferableOneWay();
                leftToLoad.Add(transferableOneWay2);
                transferableOneWay2.things.AddRange(t.things);
                transferableOneWay2.AdjustTo(count);
            }
        }

        /// <summary>
        /// 从待加载列表移除（模仿原版MapPortal.SubtractFromToLoadList）
        /// </summary>
        public int SubtractFromToLoadList(Thing t, int count)
        {
            if (leftToLoad == null)
            {
                return 0;
            }
            TransferableOneWay transferableOneWay = TransferableUtility.TransferableMatchingDesperate(t, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferableOneWay == null)
            {
                return 0;
            }
            if (transferableOneWay.CountToTransfer <= 0)
            {
                return 0;
            }
            int num = Mathf.Min(count, transferableOneWay.CountToTransfer);
            transferableOneWay.AdjustBy(-num);
            transferableOneWay.things.Remove(t);
            if (transferableOneWay.CountToTransfer <= 0)
            {
                leftToLoad.Remove(transferableOneWay);
            }
            return num;
        }

        /// <summary>
        /// 取消加载（模仿原版MapPortal.CancelLoad）
        /// </summary>
        public void CancelLoad()
        {
            Lord lord = base.Map.lordManager.lords.FirstOrDefault((Lord l) => l.LordJob is LordJob_LoadAndEnterPortal lordJob_LoadAndEnterPortal && lordJob_LoadAndEnterPortal.portal is global::WulaFallenEmpire.MapPortalAdapter adapter && adapter.shuttle == this);
            if (lord != null)
            {
                base.Map.lordManager.RemoveLord(lord);
            }
            leftToLoad.Clear();
        }

        #endregion

        #region 生命周期方法

        /// <summary>
        /// 更新退出点目标
        /// </summary>
        public void UpdateExitPointTarget()
        {
            if (exit == null) return;
            if (base.Map == null)
            {
                Log.Warning("[WULA] UpdateExitPointTarget: Shuttle map is null, cannot update exit point target.");
                return;
            }

            try
            {
                exit.targetMap = base.Map;
                exit.targetPos = base.Position;
                Log.Message($"[WULA] Updated exit point target to map {base.Map.uniqueID} at position {base.Position}");
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error updating exit point target: {ex}");
            }
        }

        /// <summary>
        /// 重写Tick方法，定期检查穿梭机状态变化和物品同步
        /// </summary>
        protected override void Tick()
        {
            base.Tick();

            // 每隔一段时间检查退出点目标是否需要更新（处理穿梭机移动的情况）
            if (this.IsHashIntervalTick(2500) && pocketMapGenerated && exit != null)
            {
                UpdateExitPointTarget();
            }

            // 定期检查并同步口袋空间中的物品（每5分钟检查一次）
            if (this.IsHashIntervalTick(18000) && pocketMapGenerated && pocketMap != null)
            {
                // 自动同步口袋空间中的物品到主容器
                try
                {
                    int itemsInPocket = pocketMap.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Item && t.def.EverHaulable && t.Spawned);
                    if (itemsInPocket > 0)
                    {
                        TransferPocketContainerToMainTransporter();
                        if (Prefs.DevMode)
                        {
                            Log.Message($"[WULA] Auto-synced pocket items. Current status: {GetPocketSpaceDebugInfo()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[WULA] Error during auto-sync: {ex}");
                }
            }
        }

        /// <summary>
        /// 重写 SpawnSetup，确保位置变化时更新退出点
        /// </summary>
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            Log.Message($"[WULA-DEBUG] Building_ArmedShuttleWithPocket.SpawnSetup START. Instance ID: {this.ThingID}, Map param: {map?.GetUniqueLoadID() ?? "null"}, Respawning: {respawningAfterLoad}");

            // 保存旧位置信息
            Map oldMap = this.Map;
            IntVec3 oldPos = this.Position;

            base.SpawnSetup(map, respawningAfterLoad);

            // 验证关键组件
            CompTransporter transporter = this.GetComp<CompTransporter>();
            if (transporter == null)
            {
                Log.Error("[WULA-ERROR] CompTransporter missing in SpawnSetup! This will cause serious issues.");
            }
            else
            {
                Log.Message($"[WULA-DEBUG] CompTransporter found with {transporter.innerContainer?.Count ?? 0} items");
            }

            // 更新退出点目标（处理穿梭机重新部署的情况）
            UpdateExitPointTarget();

            // 如果是从飞行状态恢复，重新启用传送功能
            if (transportDisabled)
            {
                Log.Message("[WULA-DEBUG] Re-enabling transport functionality after landing");
                transportDisabled = false;

                // 如果有口袋空间，确保退出点正确连接到新地图
                if (pocketMapGenerated && pocketMap != null && exit != null)
                {
                    Log.Message($"[WULA-DEBUG] Reconnecting pocket space exit to new map: {map?.GetUniqueLoadID() ?? "null"} at {this.Position}");
                }
            }

            // 从 ThingDef 中读取 portal 配置
            if (def.HasModExtension<PocketMapProperties>())
            {
                if (this.Map == null)
                {
                    Log.Error($"[WULA-ERROR] Building_ArmedShuttleWithPocket {this.ThingID} Map is NULL after SpawnSetup!");
                }
                PocketMapProperties props = def.GetModExtension<PocketMapProperties>();
                pocketMapSize = props.pocketMapSize;
                mapGenerator = props.mapGenerator;
                exitDef = props.exitDef;
                allowDirectAccess = props.allowDirectAccess;
            }
        }
    }

    public class PocketMapProperties : DefModExtension
    {
        public IntVec2 pocketMapSize = new IntVec2(80, 80);
        public MapGeneratorDef mapGenerator;
        public ThingDef exitDef;
        public bool allowDirectAccess = true;
    }

    /// <summary>
    /// 适配器类，使Building_ArmedShuttleWithPocket能够作为MapPortal被Dialog_EnterPortal使用
    /// </summary>
    public class MapPortalAdapter : MapPortal
    {
        public Building_ArmedShuttleWithPocket shuttle;

        public MapPortalAdapter() { } // Scribe需要无参数构造函数

        public MapPortalAdapter(Building_ArmedShuttleWithPocket shuttle)
        {
            this.shuttle = shuttle;
        }

        public new Map PocketMap => shuttle?.PocketMap;

        public new bool PocketMapExists => shuttle?.PocketMap != null; // 修正

        public new bool AutoDraftOnEnter => false; // 修正

        protected new Texture2D EnterTex => ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter"); // 修正

        public new string EnterString => shuttle?.EnterString;

        public new string CancelEnterString => shuttle?.CancelEnterString;

        public new string EnteringString => shuttle?.EnteringString;

        public new bool LoadInProgress => shuttle?.LoadInProgress ?? false;

        public new bool AnyPawnCanLoadAnythingNow => shuttle?.AnyPawnCanLoadAnythingNow ?? false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref shuttle, "shuttle");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            // 适配器不应该被Spawn，此方法留空或报错
            Log.Error("MapPortalAdapter should not be spawned directly.");
        }

        protected override void Tick()
        {
            // 适配器不应该Tick，此方法留空
        }

        public new ThingOwner GetDirectlyHeldThings()
        {
            return shuttle?.GetDirectlyHeldThings();
        }

        public new void GetChildHolders(List<IThingHolder> outChildren)
        {
            shuttle?.GetChildHolders(outChildren);
        }

        public new void Notify_ThingAdded(Thing t)
        {
            shuttle?.Notify_ThingAdded(t);
        }

        public new void AddToTheToLoadList(TransferableOneWay t, int count)
        {
            shuttle?.AddToTheToLoadList(t, count);
        }

        public new int SubtractFromToLoadList(Thing t, int count)
        {
            return shuttle?.SubtractFromToLoadList(t, count) ?? 0;
        }

        public new void CancelLoad()
        {
            shuttle?.CancelLoad();
        }

        public new bool IsEnterable(out string reason)
        {
            if (shuttle == null)
            {
                reason = "WULA.PocketSpace.AdapterError".Translate();
                return false;
            }
            return shuttle.IsEnterable(out reason);
        }

        public new Map GetOtherMap()
        {
            return shuttle?.GetOtherMap();
        }

        public new IntVec3 GetDestinationLocation()
        {
            return shuttle?.GetDestinationLocation() ?? IntVec3.Invalid;
        }

        public new void OnEntered(Pawn pawn)
        {
            shuttle?.OnEntered(pawn);
        }

        public new IEnumerable<Gizmo> GetGizmos()
        {
            // 适配器不直接提供Gizmo，Gizmo应该由shuttle提供
            return base.GetGizmos(); // 或者返回空的IEnumerable
        }
    }

    #endregion // MapPortal兼容接口

}