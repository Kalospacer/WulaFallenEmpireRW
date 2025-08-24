using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
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
        
        /// <summary>查看口袋地图图标</summary>
        private static readonly Texture2D ViewPocketMapTex = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave");
        
        /// <summary>取消进入图标</summary>
        private static readonly Texture2D CancelEnterTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        
        /// <summary>默认进入图标</summary>
        private static readonly Texture2D DefaultEnterTex = ContentFinder<Texture2D>.Get("UI/Commands/EnterCave");
        
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
        
        /// <summary>口袋空间内的物品容器</summary>
        private ThingOwner innerContainer;
        
        /// <summary>原版MapPortal的容器代理（必须有这个字段才能与Dialog_EnterPortal正常工作）</summary>
        public PortalContainerProxy containerProxy;
        
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
        
        /// <summary>原版MapPortal的PocketMap属性（包含自动清理无效地图的逻辑）</summary>
        public Map PocketMapForPortal
        {
            get
            {
                Map map = pocketMap;
                if (map != null && map.Parent?.HasMap == false)
                {
                    pocketMap = null;
                }
                return pocketMap;
            }
        }
        
        /// <summary>口袋地图是否存在</summary>
        public bool PocketMapExists => PocketMapForPortal != null;
        
        /// <summary>口袋地图是否已生成</summary>
        public bool PocketMapGenerated => pocketMapGenerated;
        
        /// <summary>是否允许直接访问口袋空间</summary>
        public bool AllowDirectAccess => allowDirectAccess;
        
        /// <summary>内部容器</summary>
        public ThingOwner InnerContainer => innerContainer;
        
        /// <summary>
        /// 获取进入按钮的图标
        /// </summary>
        protected virtual Texture2D EnterTex => DefaultEnterTex;
        
        /// <summary>
        /// 获取进入按钮的文本 - 专门用于人员传送
        /// </summary>
        public virtual string EnterString => "WULA.PocketSpace.EnterPawns".Translate();
        
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

        #region 构造函数

        public Building_ArmedShuttleWithPocket()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        }

        #endregion

        #region 基础重写方法



        public override void PostMake()
        {
            base.PostMake();
            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // 模仿原版MapPortal.ExposeData的逻辑
            Map map = pocketMap;
            if (map != null && map.Parent?.HasMap == false)
            {
                pocketMap = null;
            }
            
            Scribe_Deep.Look(ref pocketMap, "pocketMap");
            Scribe_Values.Look(ref pocketMapGenerated, "pocketMapGenerated", false);
            Scribe_Values.Look(ref pocketMapSize, "pocketMapSize", new IntVec2(80, 80));
            Scribe_Defs.Look(ref mapGenerator, "mapGenerator");
            Scribe_Defs.Look(ref exitDef, "exitDef");
            Scribe_Values.Look(ref allowDirectAccess, "allowDirectAccess", true);
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            
            // 模仿原版MapPortal，持久化leftToLoad和exit
            Scribe_References.Look(ref exit, "exit");
            Scribe_Values.Look(ref beenEntered, "beenEntered", defaultValue: false);
            Scribe_Collections.Look(ref leftToLoad, "leftToLoad", LookMode.Deep);
            Scribe_Values.Look(ref notifiedCantLoadMore, "notifiedCantLoadMore", false);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                {
                    innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
                }
                // 模仿原版MapPortal，清理无效的leftToLoad条目
                leftToLoad?.RemoveAll((TransferableOneWay x) => x.AnyThing == null);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // 清理口袋地图
            if (pocketMap != null && pocketMapGenerated)
            {
                try
                {
                    // 将口袋空间中的物品和人员转移到主地图
                    TransferAllFromPocketToMainMap();
                    
                    // 销毁口袋地图
                    PocketMapUtility.DestroyPocketMap(pocketMap);
                }
                catch (Exception ex)
                {
                    Log.Error($"[WULA] Error cleaning up pocket map: {ex}");
                }
            }
            base.DeSpawn(mode);
        }
        
        /// <summary>
        /// 模仿原版MapPortal.Tick方法，处理加载进度通知和穿梭机状态变化
        /// </summary>
        protected override void Tick()
        {
            base.Tick();
            
            // 模仿原版MapPortal的Tick逻辑：处理加载进度通知
            if (this.IsHashIntervalTick(60) && Spawned && LoadInProgress && !notifiedCantLoadMore && !AnyPawnCanLoadAnythingNow && leftToLoad?[0]?.AnyThing != null)
            {
                notifiedCantLoadMore = true;
                Messages.Message("MessageCantLoadMoreIntoPortal".Translate(Label, Faction.OfPlayer.def.pawnsPlural, leftToLoad[0].AnyThing), this, MessageTypeDefOf.CautionInput);
            }
            
            // 每隔一段时间检查退出点目标是否需要更新（处理穿梭机移动的情况）
            if (this.IsHashIntervalTick(2500) && pocketMapGenerated && exit != null)
            {
                UpdateExitPointTarget();
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            
            if (pocketMapGenerated)
            {
                sb.AppendLine("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.Ready".Translate());
                if (innerContainer.Count > 0)
                {
                    sb.AppendLine("WULA.PocketSpace.ItemCount".Translate(innerContainer.Count));
                }
                
                // 显示口袋空间中的人员数量
                if (pocketMap != null)
                {
                    int pawnCount = pocketMap.mapPawns.AllPawnsSpawned.Where(p => p.IsColonist).Count();
                    if (pawnCount > 0)
                    {
                        sb.AppendLine("WULA.PocketSpace.PawnCount".Translate(pawnCount));
                    }
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
                return false; // 需要特殊权限
            }
            
            if (!Spawned)
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
                if (TransferPawnToPocketSpace(pawn))
                {
                    transferredCount++;
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
                // 注意：我们不是MapPortal，所以设为null
                PocketMapUtility.currentlyGeneratingPortal = null;
                pocketMap = GeneratePocketMapInt();
                PocketMapUtility.currentlyGeneratingPortal = null;
                
                if (pocketMap != null)
                {
                    pocketMapGenerated = true;
                    
                    // 在口袋地图中心放置退出点
                    PlaceExitInPocketMap();
                    
                    Log.Message($"[WULA] Pocket map created successfully with size {pocketMap.Size}");
                }
                else
                {
                    Log.Error("[WULA] Failed to create pocket map");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error creating pocket map: {ex}");
                PocketMapUtility.currentlyGeneratingPortal = null; // 确保清理
            }
        }
        
        /// <summary>
        /// 模仿原版MapPortal.GeneratePocketMapInt
        /// </summary>
        protected virtual Map GeneratePocketMapInt()
        {
            // 使用自定义地图生成器
            if (mapGenerator == null)
            {
                mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("WULA_PocketSpace_Small", false) 
                             ?? DefDatabase<MapGeneratorDef>.GetNamed("AncientStockpile", false) 
                             ?? MapGeneratorDefOf.Base_Player;
            }
            
            // 使用自定义尺寸
            IntVec3 mapSize = new IntVec3(pocketMapSize.x, 1, pocketMapSize.z);
            
            return PocketMapUtility.GeneratePocketMap(mapSize, mapGenerator, GetExtraGenSteps(), this.Map);
        }
        
        /// <summary>
        /// 模仿原版MapPortal.GetExtraGenSteps
        /// </summary>
        protected virtual IEnumerable<GenStepWithParams> GetExtraGenSteps()
        {
            return Enumerable.Empty<GenStepWithParams>();
        }
        /// <summary>
        /// 在口袋地图中创建退出点（模仿原版）
        /// </summary>
        private void PlaceExitInPocketMap()
        {
            if (pocketMap == null || exitDef == null) return;

            try
            {
                // 在地图中心找一个合适的位置
                IntVec3 exitPos = pocketMap.Center;
                
                // 寻找可建造的位置
                if (!exitPos.Standable(pocketMap) || exitPos.GetThingList(pocketMap).Any(t => t.def.category == ThingCategory.Building))
                {
                    exitPos = CellFinder.RandomClosewalkCellNear(pocketMap.Center, pocketMap, 5, 
                        p => p.Standable(pocketMap) && !p.GetThingList(pocketMap).Any(t => t.def.category == ThingCategory.Building));
                }

                if (exitPos.IsValid)
                {
                    // 创建退出点建筑
                    Thing exitBuilding = ThingMaker.MakeThing(exitDef);
                    if (exitBuilding is Building_PocketMapExit exitPortal)
                    {
                        exitPortal.targetMap = this.Map;
                        exitPortal.targetPos = this.Position;
                        exitPortal.parentShuttle = this;
                        exit = exitPortal; // 设置 exit 引用，模仿原版 MapPortal
                    }
                    
                    GenPlace.TryPlaceThing(exitBuilding, exitPos, pocketMap, ThingPlaceMode.Direct);
                    Log.Message($"[WULA] Created exit point at {exitPos} in pocket map");
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
            if (pocketMap == null || !Spawned) return;

            try
            {
                // 转移所有殖民者
                List<Pawn> pawnsToTransfer = pocketMap.mapPawns.AllPawnsSpawned
                    .Where(p => p.IsColonist).ToList();
                    
                foreach (Pawn pawn in pawnsToTransfer)
                {
                    IntVec3 spawnPos = CellFinder.RandomClosewalkCellNear(this.Position, this.Map, 5, 
                        p => p.Standable(this.Map) && !p.GetThingList(this.Map).Any(t => t is Pawn));
                    
                    if (spawnPos.IsValid)
                    {
                        pawn.DeSpawn();
                        GenPlace.TryPlaceThing(pawn, spawnPos, this.Map, ThingPlaceMode.Near);
                    }
                }

                // 转移所有物品到内部容器
                List<Thing> itemsToTransfer = pocketMap.listerThings.AllThings
                    .Where(t => t.def.category == ThingCategory.Item && t.def.EverHaulable).ToList();
                    
                foreach (Thing item in itemsToTransfer)
                {
                    if (item.Spawned)
                    {
                        item.DeSpawn();
                        if (!innerContainer.TryAdd(item))
                        {
                            // 如果容器满了，丢到穿梭机附近
                            IntVec3 dropPos = CellFinder.RandomClosewalkCellNear(this.Position, this.Map, 3);
                            if (dropPos.IsValid)
                            {
                                GenPlace.TryPlaceThing(item, dropPos, this.Map, ThingPlaceMode.Near);
                            }
                        }
                    }
                }
                
                Log.Message($"[WULA] Transferred {pawnsToTransfer.Count} pawns and {itemsToTransfer.Count} items from pocket space");
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error transferring from pocket map: {ex}");
            }
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
                if (pocketMap == null)
                {
                    // 创建口袋空间按钮
                    Command_Action createCommand = new Command_Action();
                    createCommand.action = delegate
                    {
                        try
                        {
                            Log.Message("[WULA] Creating pocket map...");
                            CreatePocketMap();
                            
                            if (pocketMap != null)
                            {
                                Messages.Message("WULA.PocketSpace.CreationSuccess".Translate(), this, MessageTypeDefOf.PositiveEvent);
                            }
                            else
                            {
                                Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[WULA] Error creating pocket map: {ex}");
                            Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                        }
                    };
                    createCommand.icon = EnterTex;
                    createCommand.defaultLabel = "WULA.PocketSpace.CreateMap".Translate();
                    createCommand.defaultDesc = "WULA.PocketSpace.CreateMapDesc".Translate();
                    
                    // 检查是否可以创建
                    string reason;
                    createCommand.Disabled = !IsEnterable(out reason);
                    createCommand.disabledReason = reason;
                    yield return createCommand;
                }
                else
                {
                    // 进入口袋空间按钮（直接复制原版MapPortal的逻辑）
                    Command_Action enterCommand = new Command_Action();
                    enterCommand.action = delegate
                    {
                        try
                        {
                            Log.Message("[WULA] Creating MapPortalAdapter...");
                            var adapter = new MapPortalAdapter(this);
                            Log.Message($"[WULA] Adapter created. Map: {adapter.Map?.uniqueID}, Spawned: {adapter.Spawned}");
                            
                            Log.Message("[WULA] Creating Dialog_EnterPortal...");
                            Dialog_EnterPortal window = new Dialog_EnterPortal(adapter);
                            Log.Message("[WULA] Dialog created, adding to WindowStack...");
                            Find.WindowStack.Add(window);
                            Log.Message("[WULA] Dialog added to WindowStack successfully.");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[WULA] Error opening Dialog_EnterPortal: {ex}");
                            Messages.Message("WULA.PocketSpace.LoadingDialogError".Translate(), MessageTypeDefOf.RejectInput);
                        }
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
                
                // 查看口袋地图按钮（模仿原版MapPortal）
                if (pocketMap != null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "WULA.PocketSpace.ViewMap".Translate(),
                        defaultDesc = "WULA.PocketSpace.ViewMapDesc".Translate(),
                        icon = ViewPocketMapTex,
                        action = delegate
                        {
                            // 模仿原版，跳转到口袋地图并选中退出点
                            if (exit != null)
                            {
                                CameraJumper.TryJumpAndSelect(exit);
                            }
                            else
                            {
                                SwitchToPocketSpace();
                            }
                        }
                    };
                }
            }
        }



        #endregion

        #region IThingHolder接口实现

        public ThingOwner GetDirectlyHeldThings()
        {
            // 返回containerProxy，与Dialog_EnterPortal兼容
            return containerProxy;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
        
        #endregion
        
        #region MapPortal兼容接口（使Dialog_EnterPortal能正常工作）
        
        /// <summary>
        /// 检查是否可以进入（模仿原版MapPortal.IsEnterable）
        /// </summary>
        public virtual bool IsEnterable(out string reason)
        {
            if (!allowDirectAccess)
            {
                reason = "WULA.PocketSpace.AccessDenied".Translate();
                return false;
            }
            
            if (!Spawned)
            {
                reason = "WULA.PocketSpace.NotSpawned".Translate();
                return false;
            }
            
            reason = "";
            return true;
        }
        
        /// <summary>
        /// 获取目标地图（模仿原版MapPortal.GetOtherMap）
        /// </summary>
        public virtual Map GetOtherMap()
        {
            if (pocketMap == null)
            {
                CreatePocketMap();
            }
            return pocketMap;
        }
        
        /// <summary>
        /// 获取目标位置（模仿原版MapPortal.GetDestinationLocation）
        /// </summary>
        public virtual IntVec3 GetDestinationLocation()
        {
            if (exit != null)
            {
                return exit.Position;
            }
            return pocketMap?.Center ?? IntVec3.Invalid;
        }
        
        /// <summary>
        /// 处理进入事件（模仿原版MapPortal.OnEntered）
        /// </summary>
        public virtual void OnEntered(Pawn pawn)
        {
            // 通知物品被添加（用于统计和管理）
            Notify_ThingAdded(pawn);
            
            // 播放传送音效（如果存在）
            if (Find.CurrentMap == this.Map)
            {
                // 可以在这里添加音效播放
                // def.portal?.traverseSound?.PlayOneShot(this);
            }
        }
        
        #endregion
        
        #region 原版MapPortal的物品传送方法
        
        /// <summary>
        /// 通知有物品被添加（模仿原版 MapPortal.Notify_ThingAdded）
        /// </summary>
        public void Notify_ThingAdded(Thing t)
        {
            Log.Message($"[WULA] Notify_ThingAdded called for: {t?.def?.defName} x{t?.stackCount}");
            Log.Message($"[WULA] leftToLoad count before: {leftToLoad?.Count ?? 0}");
            
            int removedCount = SubtractFromToLoadList(t, t.stackCount);
            
            Log.Message($"[WULA] Removed {removedCount} items from leftToLoad list");
            Log.Message($"[WULA] leftToLoad count after: {leftToLoad?.Count ?? 0}");
            
            // 同时通知CompTransporter组件，确保原版装载系统也得到通知
            var compTransporter = this.GetComp<CompTransporter>();
            if (compTransporter != null)
            {
                Log.Message($"[WULA] Notifying CompTransporter about thing added: {t?.def?.defName}");
                try 
                {
                    // 调用CompTransporter的Notify_ThingAdded方法（如果存在）
                    var method = compTransporter.GetType().GetMethod("Notify_ThingAdded", new[] { typeof(Thing) });
                    if (method != null)
                    {
                        method.Invoke(compTransporter, new object[] { t });
                        Log.Message("[WULA] Successfully called CompTransporter.Notify_ThingAdded");
                    }
                    else
                    {
                        Log.Message("[WULA] CompTransporter.Notify_ThingAdded method not found");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WULA] Failed to notify CompTransporter: {ex.Message}");
                }
            }
            else
            {
                Log.Message("[WULA] No CompTransporter found on this building");
            }
        }
        
        /// <summary>
        /// 添加到加载列表（模仿原版 MapPortal.AddToTheToLoadList）
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
        /// 从加载列表中减去（模仿原版 MapPortal.SubtractFromToLoadList）
        /// </summary>
        public int SubtractFromToLoadList(Thing t, int count)
        {
            Log.Message($"[WULA] SubtractFromToLoadList called for: {t?.def?.defName} x{count}");
            
            if (leftToLoad == null)
            {
                Log.Message("[WULA] leftToLoad is null, returning 0");
                return 0;
            }
            
            Log.Message($"[WULA] Searching in leftToLoad list with {leftToLoad.Count} entries");
            TransferableOneWay transferableOneWay = TransferableUtility.TransferableMatchingDesperate(t, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            
            if (transferableOneWay == null)
            {
                Log.Message($"[WULA] No matching transferable found for {t?.def?.defName}");
                return 0;
            }
            
            Log.Message($"[WULA] Found matching transferable with CountToTransfer: {transferableOneWay.CountToTransfer}");
            
            if (transferableOneWay.CountToTransfer <= 0)
            {
                Log.Message("[WULA] CountToTransfer <= 0, returning 0");
                return 0;
            }
            
            int num = Mathf.Min(count, transferableOneWay.CountToTransfer);
            Log.Message($"[WULA] Adjusting transferable by: -{num}");
            
            transferableOneWay.AdjustBy(-num);
            transferableOneWay.things.Remove(t);
            
            Log.Message($"[WULA] After adjustment - CountToTransfer: {transferableOneWay.CountToTransfer}, things.Count: {transferableOneWay.things.Count}");
            
            if (transferableOneWay.CountToTransfer <= 0)
            {
                Log.Message("[WULA] Removing transferable from leftToLoad list");
                leftToLoad.Remove(transferableOneWay);
            }
            
            Log.Message($"[WULA] leftToLoad list now has {leftToLoad.Count} entries");
            return num;
        }
        
        /// <summary>
        /// 取消加载（模仿原版 MapPortal.CancelLoad）
        /// </summary>
        public void CancelLoad(MapPortal portal = null)
        {
            // 简化版本：只清理leftToLoad列表
            // 原版需要查找MapPortal相关的Lord，但我们不是MapPortal类型
            if (leftToLoad != null)
            {
                leftToLoad.Clear();
            }
        }
        


        #endregion
        
        #region 穿梭机状态变化处理
        
        /// <summary>
        /// 更新口袋空间中退出点的目标位置（处理穿梭机位置变化）
        /// </summary>
        public void UpdateExitPointTarget()
        {
            if (pocketMap == null || exit == null) return;
            
            try
            {
                // 如果退出点是我们的Building_PocketMapExit类型，更新其目标位置
                if (exit is Building_PocketMapExit pocketExit)
                {
                    // 更新目标地图和位置
                    if (this.Spawned)
                    {
                        // 穿梭机在地图上，更新目标位置
                        if (pocketExit.targetMap != this.Map || pocketExit.targetPos != this.Position)
                        {
                            pocketExit.targetMap = this.Map;
                            pocketExit.targetPos = this.Position;
                            pocketExit.parentShuttle = this;
                            Log.Message($"[WULA] Updated pocket map exit target to shuttle location: {this.Map?.uniqueID} at {this.Position}");
                        }
                    }
                    else
                    {
                        // 穿梭机不在地图上（可能在飞行中），记录警告但保持原有目标
                        Log.Warning($"[WULA] Shuttle not spawned, pocket map exit target may be outdated. Current target: {pocketExit.targetMap?.uniqueID} at {pocketExit.targetPos}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error updating exit point target: {ex}");
            }
        }
        

        
        /// <summary>
        /// 重写 SpawnSetup，确保位置变化时更新退出点
        /// </summary>
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            // 保存旧位置信息
            Map oldMap = this.Map;
            IntVec3 oldPos = this.Position;
            
            base.SpawnSetup(map, respawningAfterLoad);
            
            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            }
            
            // 初始化containerProxy（模仿原版MapPortal）
            containerProxy = new PortalContainerProxy
            {
                portal = this
            };
            
            // 更新退出点目标（处理穿梭机重新部署的情况）
            UpdateExitPointTarget();
            
            // 从 ThingDef 中读取 portal 配置
            if (def.HasModExtension<PocketMapProperties>())
            {
                var portalProps = def.GetModExtension<PocketMapProperties>();
                if (portalProps.pocketMapGenerator != null)
                {
                    mapGenerator = portalProps.pocketMapGenerator;
                }
                if (portalProps.exitDef != null)
                {
                    exitDef = portalProps.exitDef;
                }
                if (portalProps.pocketMapSize != IntVec2.Zero)
                {
                    pocketMapSize = portalProps.pocketMapSize;
                }
                allowDirectAccess = portalProps.allowDirectAccess;
            }
            
            // 初始化地图生成器和退出点定义（如果 XML 中没有配置）
            if (mapGenerator == null)
            {
                mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("AncientStockpile", false) 
                    ?? DefDatabase<MapGeneratorDef>.GetNamed("Base_Player", false)
                    ?? MapGeneratorDefOf.Base_Player;
            }
            
            if (exitDef == null)
            {
                exitDef = DefDatabase<ThingDef>.GetNamed("WULA_PocketMapExit", false) 
                    ?? ThingDefOf.Door;
            }
            
            // 如果位置发生了变化，记录日志
            if (oldMap != null && (oldMap != map || oldPos != this.Position))
            {
                Log.Message($"[WULA] Shuttle moved from {oldMap?.uniqueID}:{oldPos} to {map?.uniqueID}:{this.Position}, updating pocket map exit target");
            }
        }
        
        #endregion
    }

    /// <summary>
    /// 口袋空间属性配置类
    /// </summary>
    public class PocketMapProperties : DefModExtension
    {
        /// <summary>口袋地图生成器</summary>
        public MapGeneratorDef pocketMapGenerator;
        
        /// <summary>退出点定义</summary>
        public ThingDef exitDef;
        
        /// <summary>口袋地图大小</summary>
        public IntVec2 pocketMapSize = new IntVec2(13, 13);
        
        /// <summary>允许直接访问</summary>
        public bool allowDirectAccess = true;
    }

    /// <summary>
    /// MapPortal适配器类，使非MapPortal类型能够使用Dialog_EnterPortal
    /// 直接继承MapPortal并委托给Building_ArmedShuttleWithPocket实现
    /// </summary>
    public class MapPortalAdapter : MapPortal
    {
        private Building_ArmedShuttleWithPocket shuttleBuilding;
        
        public MapPortalAdapter(Building_ArmedShuttleWithPocket shuttle)
        {
            Log.Message($"[WULA] MapPortalAdapter constructor called for shuttle: {shuttle?.def?.defName}");
            shuttleBuilding = shuttle;
            
            // 确保基础属性正确设置
            this.def = shuttle.def ?? ThingDefOf.Wall; // 提供默认值避免null
            this.HitPoints = shuttle.HitPoints;
            
            // 关键：手动设置Map和Position属性以避免null引用
            if (shuttle.Spawned && shuttle.Map != null)
            {
                // 手动调用父类的SpawnSetup，但要小心处理
                try 
                {
                    base.SpawnSetup(shuttle.Map, false);
                    Log.Message($"[WULA] SpawnSetup completed for map: {shuttle.Map.uniqueID}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WULA] SpawnSetup failed, manually setting properties: {ex.Message}");
                    // 如果SpawnSetup失败，手动设置关键属性
                }
            }
            
            // 设置基础MapPortal属性
            this.pocketMap = shuttle.PocketMapForPortal;
            this.leftToLoad = shuttle.leftToLoad ?? new List<TransferableOneWay>();
            
            // 确保exit属性被正确设置
            // 注意：由于类型不兼容，暂时设为null，在GetDestinationLocation中处理
            this.exit = null; // 原版PocketMapExit类型与我们的Building_PocketMapExit不兼容
            
            Log.Message($"[WULA] Synced pocketMap: {pocketMap?.uniqueID}, leftToLoad count: {leftToLoad?.Count}, exit: {exit != null}");
            
            // 使用原版的PortalContainerProxy
            try 
            {
                this.containerProxy = new RimWorld.PortalContainerProxy
                {
                    portal = this
                };
                Log.Message("[WULA] Created RimWorld.PortalContainerProxy successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Failed to create RimWorld.PortalContainerProxy: {ex}");
                // 使用我们自己的实现作为回退
                Log.Message("[WULA] Using custom PortalContainerProxy as fallback");
            }
            Log.Message("[WULA] MapPortalAdapter initialization complete");
        }
        
        // 委托给shuttleBuilding的关键属性（使用new隐藏基类属性）
        // 委托给shuttleBuilding的关键属性（使用new隐藏基类属性）
        public new Map Map 
        { 
            get 
            {
                // 优先返回shuttleBuilding的Map
                if (shuttleBuilding?.Map != null)
                {
                    return shuttleBuilding.Map;
                }
                
                // 如果shuttleBuilding的Map为null，返回基类的Map
                if (base.Map != null)
                {
                    return base.Map;
                }
                
                // 最后的回退：返回当前游戏地图（避免null）
                Log.Warning("[WULA] Both shuttleBuilding.Map and base.Map are null, using Current.Game.CurrentMap as fallback");
                return Find.CurrentMap ?? Current.Game.Maps?.FirstOrDefault();
            }
        }
        public new IntVec3 Position => shuttleBuilding?.Position ?? base.Position;
        public new bool Spawned => shuttleBuilding?.Spawned ?? base.Spawned;
        public new string Label => shuttleBuilding?.Label ?? base.Label;
        
        // 委托给shuttleBuilding的关键方法（重写虚拟方法）
        public override bool IsEnterable(out string reason)
        {
            return shuttleBuilding.IsEnterable(out reason);
        }
        
        public override Map GetOtherMap()
        {
            return shuttleBuilding.GetOtherMap();
        }
        
        public override IntVec3 GetDestinationLocation()
        {
            return shuttleBuilding.GetDestinationLocation();
        }
        
        public override void OnEntered(Pawn pawn)
        {
            shuttleBuilding.OnEntered(pawn);
        }
        
        // 委托给shuttleBuilding的物品管理方法（使用new隐藏基类方法）
        public new void Notify_ThingAdded(Thing t)
        {
            shuttleBuilding.Notify_ThingAdded(t);
        }
        
        public new void AddToTheToLoadList(TransferableOneWay t, int count)
        {
            shuttleBuilding.AddToTheToLoadList(t, count);
        }
        
        public new int SubtractFromToLoadList(Thing t, int count)
        {
            return shuttleBuilding.SubtractFromToLoadList(t, count);
        }
        
        public new void CancelLoad()
        {
            // 调用shuttleBuilding的CancelLoad方法
            shuttleBuilding.CancelLoad();
        }
        
        // 重写原版MapPortal的关键属性
        public override string EnterString => shuttleBuilding.EnterString;
        public override string CancelEnterString => shuttleBuilding.CancelEnterString;
        public override string EnteringString => shuttleBuilding.EnteringString;
        
        // 隐藏LoadInProgress属性，确保Dialog_EnterPortal能正确读取
        public new bool LoadInProgress => shuttleBuilding?.LoadInProgress ?? false;
        
        // 确保SpawnSetup正确处理
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            // 调用基类的SpawnSetup来正确初始化MapPortal的基础设施
            base.SpawnSetup(map, respawningAfterLoad);
            
            // 同步关键字段
            this.pocketMap = shuttleBuilding?.PocketMapForPortal;
            this.leftToLoad = shuttleBuilding?.leftToLoad ?? new List<TransferableOneWay>();
        }
        
        // 重写AddItemsToTransferables，让Dialog_EnterPortal只处理人员
        // 因为物品已经通过装载按钮正确传送到内部空间了
        protected virtual void AddItemsToTransferables()
        {
            // 不添加任何物品，因为物品传送由装载按钮处理
            // 这样Dialog_EnterPortal只专注于人员传送
            Log.Message("[WULA] AddItemsToTransferables: Skipping items, handled by loading button");
        }
    }

    /// <summary>
    /// 专为Building_ArmedShuttleWithPocket设计的PortalContainerProxy适配器
    /// 模仿原版PortalContainerProxy的行为，但适配非-MapPortal类型
    /// </summary>
    public class PortalContainerProxy : ThingOwner
    {
        public Building_ArmedShuttleWithPocket portal;

        public override int Count => 0;

        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true)
        {
            if (TryAdd(item, canMergeWithExistingStacks))
            {
                return count;
            }
            return 0;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (portal == null) return false;
            
            Log.Message($"[WULA] PortalContainerProxy.TryAdd called for: {item?.def?.defName} x{item?.stackCount}");
            
            Map otherMap = portal.GetOtherMap();
            IntVec3 destinationLocation = portal.GetDestinationLocation();
            
            if (otherMap == null || !destinationLocation.IsValid)
            {
                Log.Warning("[WULA] PortalContainerProxy: Invalid target map or location, using inner container");
                // 如果目标地图或位置无效，将物品放入内部容器
                return portal.InnerContainer.TryAdd(item, canMergeWithExistingStacks);
            }
            
            // 关键：严格按照原版顺序 - 先通知，再传送
            // 这样能确保leftToLoad列表在物品被传送前就得到更新
            Log.Message($"[WULA] Calling portal.Notify_ThingAdded for: {item?.def?.defName} x{item?.stackCount}");
            portal.Notify_ThingAdded(item);
            
            // 传送物品到目标地图
            Log.Message($"[WULA] Transporting item to pocket map: {item?.def?.defName}");
            GenDrop.TryDropSpawn(item, destinationLocation, otherMap, ThingPlaceMode.Near, out var _);
            
            Log.Message($"[WULA] Item transport completed successfully");
            return true;
        }

        public override int IndexOf(Thing item)
        {
            return -1;
        }

        public override bool Remove(Thing item)
        {
            return false;
        }

        protected override Thing GetAt(int index)
        {
            return null;
        }
    }


}