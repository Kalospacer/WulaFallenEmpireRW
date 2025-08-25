using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 口袋空间传送门组件 - 只作为入口功能，附加在穿梭机上处理进入内部空间的逻辑
    /// </summary>
    public class CompPocketMapPortal : ThingComp
    {
        /// <summary>组件属性</summary>
        public CompProperties_PocketMapPortal Props => (CompProperties_PocketMapPortal)props;

        /// <summary>父建筑（必须是穿梭机）</summary>
        public Building_ArmedShuttleWithPocket ParentShuttle => parent as Building_ArmedShuttleWithPocket;
        
        /// <summary>MapPortal适配器，用于使用原版Dialog_EnterPortal</summary>
        private ShuttlePortalAdapter portalAdapter;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 检查父对象是否是穿梭机
            if (ParentShuttle == null)
            {
                Log.Error($"[WULA] CompPocketMapPortal attached to non-shuttle building: {parent?.def?.defName}");
            }
            else
            {
                // 创建MapPortal适配器，并设置其地图和位置信息
                portalAdapter = new ShuttlePortalAdapter(ParentShuttle);
                // 使用反射设置适配器的地图和位置，让Dialog_EnterPortal能正确访问
                if (portalAdapter != null && ParentShuttle.Spawned)
                {
                    try
                    {
                        // 使用反射设置私有字段
                        var mapField = typeof(Thing).GetField("mapIndexOrState", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var positionField = typeof(Thing).GetField("positionInt", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (mapField != null && positionField != null)
                        {
                            mapField.SetValue(portalAdapter, mapField.GetValue(ParentShuttle));
                            positionField.SetValue(portalAdapter, positionField.GetValue(ParentShuttle));
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"[WULA] Could not set adapter map/position via reflection: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// 检查穿梭机是否可以进入（仅作为入口功能）
        /// </summary>
        public bool IsEnterable(out string reason)
        {
            if (ParentShuttle == null)
            {
                reason = "WULA.PocketSpace.NotSpawned".Translate();
                return false;
            }
            
            if (!ParentShuttle.AllowDirectAccess)
            {
                reason = "WULA.PocketSpace.AccessDenied".Translate();
                return false;
            }
            
            if (!ParentShuttle.Spawned)
            {
                reason = "WULA.PocketSpace.NotSpawned".Translate();
                return false;
            }
            
            // 检查穿梭机的传送状态
            var transportDisabledField = typeof(Building_ArmedShuttleWithPocket).GetField("transportDisabled", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (transportDisabledField != null)
            {
                bool transportDisabled = (bool)transportDisabledField.GetValue(ParentShuttle);
                if (transportDisabled)
                {
                    reason = "WULA.PocketSpace.TransportDisabled".Translate();
                    return false;
                }
            }
            
            // 检查口袋地图是否存在
            if (ParentShuttle.PocketMap == null)
            {
                reason = "WULA.PocketSpace.NoTargetMap".Translate();
                return false;
            }
            
            reason = "";
            return true;
        }

        
        /// <summary>
        /// 获取组件额外的Gizmo按钮（根据口袋空间初始化状态显示不同按钮）
        /// 重写CompGetGizmosExtra方法，这样RimWorld会自动调用并显示按钮
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (ParentShuttle == null) yield break;
            
            // 检查口袋空间是否已初始化
            bool pocketMapExists = ParentShuttle.PocketMap != null;
            
            if (!pocketMapExists)
            {
                // 口袋空间未创建，显示初始化按钮
                Command_Action initializeCommand = new Command_Action();
                initializeCommand.action = delegate
                {
                    // 创建口袋空间
                    InitializePocketSpace();
                };
                initializeCommand.icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
                initializeCommand.defaultLabel = "WULA.PocketSpace.Initialize".Translate();
                initializeCommand.defaultDesc = "WULA.PocketSpace.InitializeDesc".Translate();
                
                // 检查是否可以初始化
                if (!ParentShuttle.Spawned)
                {
                    initializeCommand.Disabled = true;
                    initializeCommand.disabledReason = "WULA.PocketSpace.NotSpawned".Translate();
                }
                
                yield return initializeCommand;
            }
            else
            {
                // 穿梭机已创建，显示装载按钮（使用原版Dialog_EnterPortal）
                Command_Action enterCommand = new Command_Action();
                enterCommand.action = delegate
                {
                    // 使用和Building_PocketMapExit一模一样的Dialog_EnterPortal方法
                    if (portalAdapter != null && portalAdapter.shuttle != null)
                    {
                        var dialog = new Dialog_EnterPortal(portalAdapter);
                        Find.WindowStack.Add(dialog);
                    }
                    else
                    {
                        Log.Error("[WULA] Portal adapter or shuttle is null, recreating adapter");
                        // 重新创建适配器
                        if (ParentShuttle != null)
                        {
                            portalAdapter = new ShuttlePortalAdapter(ParentShuttle);
                            var dialog = new Dialog_EnterPortal(portalAdapter);
                            Find.WindowStack.Add(dialog);
                        }
                        else
                        {
                            Messages.Message("内部错误：穿梭机引用丢失", ParentShuttle, MessageTypeDefOf.RejectInput);
                        }
                    }
                };
                enterCommand.icon = ContentFinder<Texture2D>.Get(Props.buttonIconPath);
                enterCommand.defaultLabel = Props.enterButtonTextKey.Translate() + "...";
                enterCommand.defaultDesc = Props.enterButtonDescKey.Translate();
                
                // 检查是否可以进入
                string reason;
                enterCommand.Disabled = !IsEnterable(out reason);
                enterCommand.disabledReason = reason;
                yield return enterCommand;
                
                // 查看口袋地图按钮
                yield return new Command_Action
                {
                    defaultLabel = "WULA.PocketSpace.SwitchTo".Translate(),
                    defaultDesc = "WULA.PocketSpace.SwitchToDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave"),
                    action = delegate
                    {
                        Current.Game.CurrentMap = ParentShuttle.PocketMap;
                        Find.CameraDriver.JumpToCurrentMapLoc(ParentShuttle.PocketMap.Center);
                    }
                };
            }
        }
        
        /// <summary>
        /// 初始化口袋空间
        /// </summary>
        private void InitializePocketSpace()
        {
            if (ParentShuttle == null || !ParentShuttle.Spawned)
            {
                Messages.Message("WULA.PocketSpace.CannotInitialize".Translate(), ParentShuttle, MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (ParentShuttle.PocketMap != null)
            {
                Messages.Message("WULA.PocketSpace.AlreadyInitialized".Translate(), ParentShuttle, MessageTypeDefOf.RejectInput);
                return;
            }
            
            try
            {
                Log.Message("[WULA] Starting pocket space initialization via component");
                
                // 使用穿梭机的SwitchToPocketSpace方法，它会自动创建口袋空间
                ParentShuttle.SwitchToPocketSpace();
                
                if (ParentShuttle.PocketMap != null)
                {
                    Messages.Message("WULA.PocketSpace.InitializeSuccess".Translate(), ParentShuttle, MessageTypeDefOf.PositiveEvent);
                    Log.Message("[WULA] Pocket space initialization completed successfully");
                }
                else
                {
                    Messages.Message("WULA.PocketSpace.InitializeFailed".Translate(), ParentShuttle, MessageTypeDefOf.RejectInput);
                    Log.Error("[WULA] Pocket space initialization failed");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error during pocket space initialization: {ex}");
                Messages.Message("WULA.PocketSpace.InitializeFailed".Translate(), ParentShuttle, MessageTypeDefOf.RejectInput);
            }
        }

    }

    /// <summary>
    /// 口袋空间传送门组件属性
    /// </summary>
    public class CompProperties_PocketMapPortal : CompProperties
    {
        /// <summary>进入按钮文本键</summary>
        public string enterButtonTextKey = "WULA.PocketSpace.Enter";
        
        /// <summary>进入按钮描述键</summary>
        public string enterButtonDescKey = "WULA.PocketSpace.EnterDesc";
        
        /// <summary>按钮图标路径</summary>
        public string buttonIconPath = "UI/Commands/LoadTransporter";
        
        public CompProperties_PocketMapPortal()
        {
            this.compClass = typeof(CompPocketMapPortal);
        }
    }
    
    /// <summary>
    /// MapPortal适配器类，将Building_ArmedShuttleWithPocket适配为MapPortal接口
    /// 完全模仿Building_PocketMapExit的实现方式
    /// </summary>
    public class ShuttlePortalAdapter : MapPortal
    {
        /// <summary>关联的穿梭机</summary>
        public Building_ArmedShuttleWithPocket shuttle;
        
        /// <summary>
        /// 默认构造函数（RimWorld组件系统要求）
        /// </summary>
        public ShuttlePortalAdapter()
        {
            // 为空，在PostSpawnSetup中初始化
        }
        
        public ShuttlePortalAdapter(Building_ArmedShuttleWithPocket shuttle)
        {
            this.shuttle = shuttle;
        }
        
        /// <summary>
        /// 重写获取其他地图，返回口袋空间（模仿Building_PocketMapExit.GetOtherMap）
        /// </summary>
        public override Map GetOtherMap()
        {
            if (shuttle?.PocketMap == null)
            {
                // 如果口袋空间还没创建，先创建它
                shuttle?.SwitchToPocketSpace();
            }
            return shuttle?.PocketMap;
        }
        
        /// <summary>
        /// 重写获取目标位置，返回口袋空间中心（模仿Building_PocketMapExit.GetDestinationLocation）
        /// </summary>
        public override IntVec3 GetDestinationLocation()
        {
            return shuttle?.PocketMap?.Center ?? IntVec3.Invalid;
        }
        
        /// <summary>
        /// 重写是否可进入，检查穿梭机状态（模仿Building_PocketMapExit.IsEnterable）
        /// </summary>
        public override bool IsEnterable(out string reason)
        {
            if (shuttle == null || !shuttle.Spawned)
            {
                reason = "WULA.PocketSpace.NotSpawned".Translate();
                return false;
            }
            
            if (!shuttle.AllowDirectAccess)
            {
                reason = "WULA.PocketSpace.AccessDenied".Translate();
                return false;
            }
            
            // 检查穿梭机的传送状态
            var transportDisabledField = typeof(Building_ArmedShuttleWithPocket).GetField("transportDisabled", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (transportDisabledField != null)
            {
                bool transportDisabled = (bool)transportDisabledField.GetValue(shuttle);
                if (transportDisabled)
                {
                    reason = "WULA.PocketSpace.TransportDisabled".Translate();
                    return false;
                }
            }
            
            reason = "";
            return true;
        }
        
        /// <summary>
        /// 重写进入事件，处理进入口袋空间（模仿Building_PocketMapExit.OnEntered）
        /// </summary>
        public override void OnEntered(Pawn pawn)
        {
            // 通知穿梭机有物品被添加（用于统计和管理）
            shuttle?.Notify_ThingAdded(pawn);
            
            // 播放传送音效（如果存在）
            if (Find.CurrentMap == shuttle?.Map)
            {
                // 可以在这里添加音效播放
                // def.portal?.traverseSound?.PlayOneShot(this);
            }
        }
        
        /// <summary>
        /// 重写进入按钮文本
        /// </summary>
        public override string EnterString => "WULA.PocketSpace.Enter".Translate();
        
        /// <summary>
        /// 重写进入按钮图标，使用装载按钮的贴图
        /// </summary>
        protected override Texture2D EnterTex => ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
        
        /// <summary>
        /// 获取地图引用（用于Dialog_EnterPortal）
        /// </summary>
        public new Map Map => shuttle?.Map;
        
        /// <summary>
        /// 获取位置引用（用于Dialog_EnterPortal）
        /// </summary>
        public new IntVec3 Position => shuttle?.Position ?? IntVec3.Invalid;
        
        /// <summary>
        /// 获取定义引用（用于Dialog_EnterPortal）
        /// </summary>
        public new ThingDef def => shuttle?.def;
    }
}