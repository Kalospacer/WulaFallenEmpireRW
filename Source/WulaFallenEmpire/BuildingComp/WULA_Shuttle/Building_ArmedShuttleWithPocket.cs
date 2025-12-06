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
using WulaFallenEmpire;
namespace WulaFallenEmpire
{
    /// <summary>
    /// 内置空间武装穿梭机 - 基于原版MapPortal机制的口袋空间实现
    /// 结合了武装防御能力和口袋空间技术的复合型载具
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_ArmedShuttleWithPocket : Building_ArmedShuttle
    {
        #region 静态图标定义（使用原版MapPortal的图标）
        
        /// <summary>查看口袋地图图标</summary>
        private static readonly Texture2D ViewPocketMapTex = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_View_ArmedShuttle_Pocket");
        
        /// <summary>默认进入图标</summary>
        private static readonly Texture2D DefaultEnterTex = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_Enter_ArmedShuttle_Pocket");
        
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

        #region 构造函数

        public Building_ArmedShuttleWithPocket()
        {
            Log.Message("[WULA-DEBUG] Building_ArmedShuttleWithPocket constructor called");
            // 不再初始化innerContainer，只使用CompTransporter的容器
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
            Scribe_References.Look(ref pocketMap, "pocketMap");
            Scribe_Values.Look(ref pocketMapGenerated, "pocketMapGenerated", false);
            Scribe_Values.Look(ref pocketMapSize, "pocketMapSize", new IntVec2(80, 80));
            Scribe_Defs.Look(ref mapGenerator, "mapGenerator");
            Scribe_Defs.Look(ref exitDef, "exitDef");
            Scribe_Values.Look(ref allowDirectAccess, "allowDirectAccess", true);
            Scribe_Values.Look(ref transportDisabled, "transportDisabled", false);
            
            // 不再序列化innerContainer，只使用CompTransporter的容器
            
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
                // 发射时暂停传送功能，但保留口袋空间
                transportDisabled = true;
                if (pocketMap != null && exit != null)
                {
                    // 标记传送功能暂停
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
            // 只在真正销毁时删除口袋空间
            switch (mode)
            {
                case DestroyMode.Vanish:  // 发射时使用，保留口袋空间
                    return false;
                case DestroyMode.Deconstruct:  // 拆除，删除口袋空间
                    return true;
                case DestroyMode.KillFinalize:  // 被摧毁，删除口袋空间  
                    return true;
                case DestroyMode.Cancel:  // 取消建造，删除口袋空间
                    return true;
                case DestroyMode.Refund:  // 退款，删除口袋空间
                    return true;
                case DestroyMode.FailConstruction:  // 建造失败，删除口袋空间
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
                
                // 显示主容器中的物品数量
                CompTransporter transporter = this.GetComp<CompTransporter>();
                int mainContainerItems = transporter?.innerContainer?.Count ?? 0;
                
                if (mainContainerItems > 0)
                {
                    sb.AppendLine($"容器物品: {mainContainerItems}");
                }
                
                // 显示口袋空间中的物品和人员数量
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
                
                // 在开发模式下显示详细调试信息
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
                return false; // 需要特殊权限
            }
            
            if (!Spawned)
            {
                return false;
            }
            
            if (transportDisabled)
            {
                return false; // 飞行中禁用传送功能
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
            // [核心修复] 将 sourceMap 设置为 null，彻底斩断口袋地图与创建它的主地图的生命周期联系。
            return PocketMapUtility.GeneratePocketMap(new IntVec3(pocketMapSize.x, 1, pocketMapSize.z), mapGenerator, null, null);
        }
        
        /// <summary>
        /// 获取额外的生成步骤（模仿 MapPortal.GetExtraGenSteps）
        /// </summary>

        /// <summary>
        /// 在口袋地图中创建退出点（模仿原版）
        /// </summary>
        private void CreateExitPoint()
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
        public bool TransferPawnToPocketSpace(Pawn pawn)
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
                // 获取穿梭机的 CompTransporter
                CompTransporter transporter = this.GetComp<CompTransporter>();
                if (transporter == null)
                {
                    Log.Error("[WULA-ERROR] CompTransporter not found on shuttle! Cannot transfer items.");
                    return;
                }
                
                Log.Message($"[WULA-DEBUG] Found CompTransporter with {transporter.innerContainer.Count} existing items");

                // 转移所有殖民者
                List<Pawn> pawnsToTransfer = pocketMap.mapPawns.AllPawnsSpawned.ToList();
                    
                Log.Message($"[WULA-DEBUG] Found {pawnsToTransfer.Count} colonists to transfer");
                    
                foreach (Pawn pawn in pawnsToTransfer)
                {
                    if (pawn.Spawned)
                    {
                        Log.Message($"[WULA-DEBUG] Transferring pawn: {pawn.LabelShort}");
                        pawn.DeSpawn();
                        
                        // 直接放入穿梭机的容器，如果失败就放到地面
                        if (!transporter.innerContainer.TryAdd(pawn))
                        {
                            Log.Warning($"[WULA-WARNING] Container full, placing pawn {pawn.LabelShort} near shuttle");
                            // 如果容器满了，放到穿梭机附近
                            IntVec3 spawnPos = CellFinder.RandomClosewalkCellNear(this.Position, this.Map, 5, 
                                p => p.Standable(this.Map) && !p.GetThingList(this.Map).Any(t => t is Pawn));
                            
                            if (spawnPos.IsValid)
                            {
                                GenPlace.TryPlaceThing(pawn, spawnPos, this.Map, ThingPlaceMode.Near);
                                Log.Message($"[WULA-DEBUG] Placed pawn {pawn.LabelShort} at {spawnPos}");
                            }
                            else
                            {
                                Log.Error($"[WULA-ERROR] Could not find valid position for pawn {pawn.LabelShort}");
                            }
                        }
                        else
                        {
                            Log.Message($"[WULA-DEBUG] Successfully added pawn {pawn.LabelShort} to container");
                        }
                    }
                }

                // 转移所有物品到穿梭机的容器
                List<Thing> itemsToTransfer = pocketMap.listerThings.AllThings
                    .Where(t => t.def.category == ThingCategory.Item && t.def.EverHaulable).ToList();
                    
                Log.Message($"[WULA-DEBUG] Found {itemsToTransfer.Count} items to transfer");
                    
                foreach (Thing item in itemsToTransfer)
                {
                    if (item.Spawned)
                    {
                        Log.Message($"[WULA-DEBUG] Transferring item: {item.LabelShort} (stack: {item.stackCount})");
                        item.DeSpawn();
                        
                        // 直接使用穿梭机的主容器
                        if (!transporter.innerContainer.TryAdd(item))
                        {
                            Log.Warning($"[WULA-WARNING] Container full, dropping item {item.LabelShort} near shuttle");
                            // 如果容器满了，丢到穿梭机附近（玩家可以手动重新装载）
                            IntVec3 dropPos = CellFinder.RandomClosewalkCellNear(this.Position, this.Map, 3);
                            if (dropPos.IsValid)
                            {
                                GenPlace.TryPlaceThing(item, dropPos, this.Map, ThingPlaceMode.Near);
                                Messages.Message($"容器已满：{item.LabelShort} 被放置在穿梭机附近", this, MessageTypeDefOf.CautionInput);
                                Log.Message($"[WULA-DEBUG] Dropped item {item.LabelShort} at {dropPos}");
                            }
                            else
                            {
                                Log.Error($"[WULA-ERROR] Could not find valid drop position for item {item.LabelShort}");
                            }
                        }
                        else
                        {
                            Log.Message($"[WULA-DEBUG] Successfully added item {item.LabelShort} to container");
                        }
                    }
                }
                
                Log.Message($"[WULA-DEBUG] Transfer complete. Container now has {transporter.innerContainer.Count} total items");
                Log.Message($"[WULA-SUCCESS] Transferred {pawnsToTransfer.Count} pawns and {itemsToTransfer.Count} items from pocket space");
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA-ERROR] Error transferring from pocket map: {ex}");
                Log.Error($"[WULA-ERROR] Stack trace: {ex.StackTrace}");
            }
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
                // “进入”按钮
                yield return new Command_Action
                {
                    icon = DefaultEnterTex,
                    defaultLabel = "WULA.PocketSpace.Enter".Translate(),
                    defaultDesc = "WULA.PocketSpace.EnterDesc".Translate(),
                    action = delegate
                    {
                        OpenPawnSelectionDialog();
                    },
                    hotKey = KeyBindingDefOf.Misc1
                };

                // “查看地图”按钮
                yield return new Command_Action
                {
                    icon = ViewPocketMapTex,
                    defaultLabel = "WULA.PocketSpace.ViewMap".Translate(),
                    defaultDesc = "WULA.PocketSpace.ViewMapDesc".Translate(),
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
                    },
                    hotKey = KeyBindingDefOf.Misc2
                };
            }
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
            
            if (transportDisabled)
            {
                reason = "WULA.PocketSpace.TransportDisabled".Translate();
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
        
        /// <summary>
        /// 打开殖民者选择对话框（模仿原版Dialog_EnterPortal的功能）
        /// </summary>
        private void OpenPawnSelectionDialog()
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

            // 打开新的穿梭机传输对话框
            Find.WindowStack.Add(new Dialog_ArmedShuttleTransfer(this));
        }
        
        #endregion
        
        #region 原版MapPortal的物品传送方法
        
        /// <summary>
        /// 通知有物品被添加（模仿原版 MapPortal.Notify_ThingAdded）
        /// </summary>
        public void Notify_ThingAdded(Thing t)
        {
            SubtractFromToLoadList(t, t.stackCount);
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
        /// 取消加载（模仿原版 MapPortal.CancelLoad）
        /// </summary>
        public void CancelLoad()
        {
            // 简化版本，只清理列表
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
            if (this.IsHashIntervalTick(18000) && pocketMapGenerated && pocketMap != null) // 18000 ticks = 5 minutes
            {
                // 自动同步口袋空间中的物品到主容器
                try
                {
                    int itemsInPocket = pocketMap.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Item && t.def.EverHaulable && t.Spawned);
                    if (itemsInPocket > 0)
                    {
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
            Log.Message($"[WULA-DEBUG] SpawnSetup called: map={map?.uniqueID}, respawning={respawningAfterLoad}");
            
            // 保存旧位置信息
            Map oldMap = this.Map;
            IntVec3 oldPos = this.Position;
            
            base.SpawnSetup(map, respawningAfterLoad);

            // [核心修复] 当穿梭机降落时，恢复其口袋地图的父级和在游戏中的注册状态
            if (pocketMap != null && pocketMapGenerated)
            {
                // 验证口袋地图的父级对象是否存在于世界列表中
                if (pocketMap.Parent is PocketMapParent pocketParent && !Find.World.pocketMaps.Contains(pocketParent))
                {
                    Log.Warning($"[WULA] Pocket map parent for map ID {pocketMap.uniqueID} was not found in the world list. Re-adding it to prevent data loss.");
                    Find.World.pocketMaps.Add(pocketParent);
                }

                // 验证口袋地图本身是否存在于游戏地图列表中
                if (!Find.Maps.Contains(pocketMap))
                {
                    Log.Warning($"[WULA] Pocket map ID {pocketMap.uniqueID} was not found in the game's map list. Re-registering it.");
                    
                    // 在重新添加前，进行安全检查，防止添加已损坏的地图
                    if (!Find.Maps.Contains(pocketMap) && (pocketMap.mapPawns == null || pocketMap.Tile < 0))
                    {
                        Log.Error("[WULA] Cannot re-register a corrupted pocket map. The contents of the pocket space are likely lost. This is a critical error.");
                        Messages.Message("WULA.PocketSpace.MapInvalidAndRecovering".Translate(), this, MessageTypeDefOf.NegativeEvent);
                        pocketMap = null;
                        pocketMapGenerated = false;
                    }
                    else
                    {
                        // 重新注册地图，使其再次“激活”
                        Current.Game.AddMap(pocketMap);
                        Log.Message($"[WULA] Pocket map {pocketMap.uniqueID} successfully re-registered.");
                    }
                }
            }
            
            // 更新退出点目标，确保它指向当前的新地图
            UpdateExitPointTarget();

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
            
            // 如果是从飞行状态恢复，重新启用传送功能
            if (transportDisabled)
            {
                Log.Message("[WULA-DEBUG] Re-enabling transport functionality after landing");
                transportDisabled = false;
                
                // 如果有口袋空间，确保退出点正确连接到新地图
                if (pocketMapGenerated && pocketMap != null && exit != null)
                {
                    Log.Message($"[WULA-DEBUG] Reconnecting pocket space exit to new map: {map?.uniqueID} at {this.Position}");
                    // 退出点会在 UpdateExitPointTarget 中自动更新
                }
            }
            
            // 从 ThingDef 中读取 portal 配置
            if (def.HasModExtension<PocketMapProperties>())
            {
                var portalProps = def.GetModExtension<PocketMapProperties>();
                Log.Message($"[WULA-DEBUG] Loading portal properties from ThingDef");
                
                if (portalProps.pocketMapGenerator != null)
                {
                    mapGenerator = portalProps.pocketMapGenerator;
                    Log.Message($"[WULA-DEBUG] Set mapGenerator: {mapGenerator.defName}");
                }
                if (portalProps.exitDef != null)
                {
                    exitDef = portalProps.exitDef;
                    Log.Message($"[WULA-DEBUG] Set exitDef: {exitDef.defName}");
                }
                if (portalProps.pocketMapSize != IntVec2.Zero)
                {
                    pocketMapSize = portalProps.pocketMapSize;
                    Log.Message($"[WULA-DEBUG] Set pocketMapSize: {pocketMapSize}");
                }
                allowDirectAccess = portalProps.allowDirectAccess;
                Log.Message($"[WULA-DEBUG] Set allowDirectAccess: {allowDirectAccess}");
            }
            
            // 初始化地图生成器和退出点定义（如果 XML 中没有配置）
            if (mapGenerator == null)
            {
                mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("AncientStockpile", false) 
                    ?? DefDatabase<MapGeneratorDef>.GetNamed("Base_Player", false)
                    ?? MapGeneratorDefOf.Base_Player;
                Log.Message($"[WULA-DEBUG] Using fallback mapGenerator: {mapGenerator.defName}");
            }
            
            if (exitDef == null)
            {
                exitDef = DefDatabase<ThingDef>.GetNamed("WULA_PocketMapExit", false) 
                    ?? ThingDefOf.Door;
                Log.Message($"[WULA-DEBUG] Using fallback exitDef: {exitDef.defName}");
            }
            
            // 如果位置发生了变化，记录日志
            if (oldMap != null && (oldMap != map || oldPos != this.Position))
            {
                Log.Message($"[WULA-DEBUG] Shuttle moved from {oldMap?.uniqueID}:{oldPos} to {map?.uniqueID}:{this.Position}, updating pocket map exit target");
            }
            
            Log.Message($"[WULA-DEBUG] SpawnSetup completed successfully");
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
}