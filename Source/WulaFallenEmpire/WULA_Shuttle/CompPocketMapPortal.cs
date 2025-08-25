using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 口袋空间传送门组件 - 将Building_PocketMapExit的功能转换成可挂载的组件
    /// 直接挂载在穿梭机上处理进入内部空间的逻辑
    /// </summary>
    public class CompPocketMapPortal : ThingComp
    {
        /// <summary>目标地图（口袋空间）</summary>
        public Map targetMap;
        
        /// <summary>目标位置（在口袋空间中的位置）</summary>
        public IntVec3 targetPos;
        
        /// <summary>父穿梭机引用</summary>
        public Building_ArmedShuttleWithPocket parentShuttle;
        
        /// <summary>组件属性</summary>
        public CompProperties_PocketMapPortal Props => (CompProperties_PocketMapPortal)props;

        /// <summary>父建筑（应该是穿梭机）</summary>
        public Building_ArmedShuttleWithPocket ParentShuttle
        {
            get
            {
                if (parentShuttle == null && parent is Building_ArmedShuttleWithPocket shuttle)
                {
                    parentShuttle = shuttle;
                }
                return parentShuttle;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Values.Look(ref targetPos, "targetPos");
            Scribe_References.Look(ref parentShuttle, "parentShuttle");
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 确保父穿梭机引用正确
            if (parent is Building_ArmedShuttleWithPocket shuttle)
            {
                parentShuttle = shuttle;
                Log.Message($"[WULA] CompPocketMapPortal attached to shuttle: {parent.LabelShort}");
            }
            else
            {
                Log.Error($"[WULA] CompPocketMapPortal attached to non-shuttle building: {parent?.def?.defName}");
            }
        }
        
        /// <summary>
        /// 设置口袋空间目标（由穿梭机调用）
        /// </summary>
        public void SetPocketSpaceTarget(Map pocketMap, IntVec3 exitPos)
        {
            targetMap = pocketMap;
            targetPos = exitPos;
            Log.Message($"[WULA] CompPocketMapPortal target set to pocket map: {pocketMap?.uniqueID} at {exitPos}");
        }
        
        /// <summary>
        /// 获取其他地图（口袋空间），模仿原版MapPortal.GetOtherMap
        /// </summary>
        public Map GetOtherMap()
        {
            // 如果没有目标地图，尝试从父穿梭机获取
            if (targetMap == null && ParentShuttle != null)
            {
                targetMap = ParentShuttle.PocketMap;
            }
            return targetMap;
        }
        
        /// <summary>
        /// 获取目标位置（在口袋空间中的位置），模仿原版MapPortal.GetDestinationLocation
        /// </summary>
        public IntVec3 GetDestinationLocation()
        {
            // 如果没有目标位置，使用口袋地图中心
            if (targetPos == IntVec3.Invalid && targetMap != null)
            {
                targetPos = targetMap.Center;
            }
            return targetPos;
        }
        
        /// <summary>
        /// 检查是否可以进入口袋空间，模仿原版MapPortal.IsEnterable
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
            
            // 检查父穿梭机的传送状态
            if (ParentShuttle != null)
            {
                // 使用反射获取 transportDisabled 字段值
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
            }
            
            // 检查目标地图是否存在
            Map pocketMap = GetOtherMap();
            if (pocketMap == null)
            {
                reason = "WULA.PocketSpace.NoTargetMap".Translate();
                return false;
            }
            
            reason = "";
            return true;
        }
        
        /// <summary>
        /// 处理进入事件，将Pawn传送到口袋空间，模仿原版MapPortal.OnEntered
        /// </summary>
        public void OnEntered(Pawn pawn)
        {
            Map pocketMap = GetOtherMap();
            if (pocketMap == null || !pawn.Spawned) return;

            try
            {
                // 在口袋地图找一个安全位置
                IntVec3 spawnPos = GetDestinationLocation();
                if (spawnPos == IntVec3.Invalid)
                {
                    spawnPos = pocketMap.Center;
                }
                
                // 寻找可行走的位置
                spawnPos = CellFinder.RandomClosewalkCellNear(spawnPos, pocketMap, 10, 
                    p => p.Standable(pocketMap) && !p.GetThingList(pocketMap).Any(t => t is Pawn));

                if (spawnPos.IsValid)
                {
                    // 传送人员到口袋空间
                    pawn.DeSpawn();
                    GenPlace.TryPlaceThing(pawn, spawnPos, pocketMap, ThingPlaceMode.Near);
                    
                    // 通知父穿梭机有物品被添加
                    if (ParentShuttle != null)
                    {
                        ParentShuttle.Notify_ThingAdded(pawn);
                    }
                    
                    // 如果是玩家控制的殖民者，切换到口袋地图
                    if (pawn.IsColonistPlayerControlled)
                    {
                        Current.Game.CurrentMap = pocketMap;
                        Find.CameraDriver.JumpToCurrentMapLoc(spawnPos);
                    }

                    Messages.Message("WULA.PocketSpace.TransferSuccess".Translate(1), MessageTypeDefOf.PositiveEvent);
                    Log.Message($"[WULA] Transferred {pawn.LabelShort} to pocket space at {spawnPos}");
                }
                else
                {
                    Log.Error($"[WULA] Could not find valid spawn position in pocket space for {pawn.LabelShort}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error entering pocket space: {ex}");
            }
        }
        
        /// <summary>
        /// 处理从口袋空间退出到主地图的逻辑
        /// </summary>
        public void ExitPocketSpace(Pawn pawn)
        {
            if (ParentShuttle == null || !ParentShuttle.Spawned || !pawn.Spawned) return;

            try
            {
                // 在主地图找一个安全位置（穿梭机附近）
                IntVec3 exitPos = CellFinder.RandomClosewalkCellNear(ParentShuttle.Position, ParentShuttle.Map, 3, 
                    p => p.Standable(ParentShuttle.Map) && !p.GetThingList(ParentShuttle.Map).Any(t => t is Pawn));
                
                if (exitPos.IsValid)
                {
                    // 传送人员回主地图
                    pawn.DeSpawn();
                    GenPlace.TryPlaceThing(pawn, exitPos, ParentShuttle.Map, ThingPlaceMode.Near);
                    
                    // 如果是玩家控制的殖民者，切换到主地图
                    if (pawn.IsColonistPlayerControlled)
                    {
                        Current.Game.CurrentMap = ParentShuttle.Map;
                        Find.CameraDriver.JumpToCurrentMapLoc(exitPos);
                    }

                    Messages.Message("WULA.PocketSpace.ExitSuccess".Translate(pawn.LabelShort), MessageTypeDefOf.PositiveEvent);
                    Log.Message($"[WULA] {pawn.LabelShort} exited pocket space to main map at {exitPos}");
                }
                else
                {
                    Log.Error($"[WULA] Could not find valid exit position for {pawn.LabelShort}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error exiting pocket space: {ex}");
            }
        }
        
        /// <summary>
        /// 获取Gizmo按钮（进入口袋空间按钮）
        /// </summary>
        public IEnumerable<Gizmo> GetGizmos()
        {
            if (ParentShuttle == null || !ParentShuttle.AllowDirectAccess) yield break;
            
            // 进入口袋空间按钮
            Command_Action enterCommand = new Command_Action();
            enterCommand.action = delegate
            {
                // 使用穿梭机的殖民者选择对话框
                if (ParentShuttle != null)
                {
                    // 获取所有可用的殖民者
                    List<Pawn> availablePawns = ParentShuttle.Map.mapPawns.AllPawnsSpawned
                        .Where(p => p.IsColonist && !p.Downed && p.CanReach(ParentShuttle, PathEndMode.Touch, Danger.Deadly))
                        .ToList();
                    
                    if (availablePawns.Count == 0)
                    {
                        Messages.Message("WULA.PocketSpace.NoPawnsAvailable".Translate(), ParentShuttle, MessageTypeDefOf.RejectInput);
                        return;
                    }
                    
                    // 创建选项列表
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    
                    // 添加单个殖民者选项
                    foreach (Pawn pawn in availablePawns)
                    {
                        FloatMenuOption option = new FloatMenuOption(
                            $"{pawn.LabelShort}", 
                            delegate
                            {
                                OnEntered(pawn);
                            }
                        );
                        options.Add(option);
                    }
                    
                    // 添加"全部殖民者"选项
                    if (availablePawns.Count > 1)
                    {
                        FloatMenuOption allOption = new FloatMenuOption(
                            "WULA.PocketSpace.AllColonists".Translate(availablePawns.Count),
                            delegate
                            {
                                foreach (Pawn pawn in availablePawns)
                                {
                                    OnEntered(pawn);
                                }
                            }
                        );
                        options.Add(allOption);
                    }
                    
                    // 显示浮动菜单
                    FloatMenu floatMenu = new FloatMenu(options);
                    Find.WindowStack.Add(floatMenu);
                }
            };
            enterCommand.icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
            enterCommand.defaultLabel = "WULA.PocketSpace.Enter".Translate() + "...";
            enterCommand.defaultDesc = "WULA.PocketSpace.EnterDesc".Translate();
            
            // 检查是否可以进入
            string reason;
            enterCommand.Disabled = !IsEnterable(out reason);
            enterCommand.disabledReason = reason;
            yield return enterCommand;
            
            // 查看口袋地图按钮
            Map pocketMap = GetOtherMap();
            if (pocketMap != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA.PocketSpace.SwitchTo".Translate(),
                    defaultDesc = "WULA.PocketSpace.SwitchToDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave"),
                    action = delegate
                    {
                        Current.Game.CurrentMap = pocketMap;
                        Find.CameraDriver.JumpToCurrentMapLoc(GetDestinationLocation());
                    }
                };
            }
        }
        
        /// <summary>
        /// 获取检视字符串信息
        /// </summary>
        public string GetInspectString()
        {
            if (ParentShuttle == null) return "";
            
            List<string> info = new List<string>();
            
            // 口袋空间状态
            if (targetMap != null)
            {
                info.Add("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.Ready".Translate());
                
                // 显示口袋空间中的人员数量
                int pawnCount = targetMap.mapPawns.AllPawnsSpawned.Where(p => p.IsColonist).Count();
                if (pawnCount > 0)
                {
                    info.Add("WULA.PocketSpace.PawnCount".Translate(pawnCount));
                }
            }
            else
            {
                info.Add("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.NotGenerated".Translate());
            }
            
            return string.Join("\n", info);
        }
    }

    /// <summary>
    /// 口袋空间传送门组件属性
    /// </summary>
    public class CompProperties_PocketMapPortal : CompProperties
    {
        public CompProperties_PocketMapPortal()
        {
            this.compClass = typeof(CompPocketMapPortal);
        }
    }
}