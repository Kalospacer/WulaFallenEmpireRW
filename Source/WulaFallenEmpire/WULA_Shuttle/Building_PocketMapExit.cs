using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 口袋空间退出点建筑 - 继承自MapPortal以获得完整的双向传送功能
    /// </summary>
    public class Building_PocketMapExit : MapPortal
    {
        /// <summary>目标地图</summary>
        public Map targetMap;
        
        /// <summary>目标位置</summary>
        public IntVec3 targetPos;
        
        /// <summary>父穿梭机</summary>
        public Building_ArmedShuttleWithPocket parentShuttle;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Values.Look(ref targetPos, "targetPos");
            Scribe_References.Look(ref parentShuttle, "parentShuttle");
        }
        
        /// <summary>
        /// 重写获取其他地图，返回主地图（模仿原版MapPortal.GetOtherMap）
        /// </summary>
        public override Map GetOtherMap()
        {
            // 动态更新目标地图，处理穿梭机移动的情况
            UpdateTargetFromParentShuttle();
            return targetMap;
        }
        
        /// <summary>
        /// 重写获取目标位置，返回主地图上的穿梭机位置（模仿原版MapPortal.GetDestinationLocation）
        /// </summary>
        public override IntVec3 GetDestinationLocation()
        {
            // 动态更新目标位置，处理穿梭机移动的情况
            UpdateTargetFromParentShuttle();
            return targetPos;
        }
        
        /// <summary>
        /// 从父穿梭机动态更新目标位置，处理穿梭机移动的情况
        /// </summary>
        private void UpdateTargetFromParentShuttle()
        {
            if (parentShuttle != null && parentShuttle.Spawned)
            {
                // 如果穿梭机还在地图上，更新目标位置
                if (targetMap != parentShuttle.Map || targetPos != parentShuttle.Position)
                {
                    targetMap = parentShuttle.Map;
                    targetPos = parentShuttle.Position;
                    Log.Message($"[WULA] Updated exit target to shuttle location: {targetMap?.uniqueID} at {targetPos}");
                }
            }
            else if (parentShuttle != null && !parentShuttle.Spawned)
            {
                // 穿梭机不在地图上（可能在飞行中）
                // 保持原有目标，但记录警告
                if (this.IsHashIntervalTick(2500)) // 每隔一段时间检查一次
                {
                    Log.Warning($"[WULA] Parent shuttle is not spawned, exit target may be outdated. Last known: {targetMap?.uniqueID} at {targetPos}");
                }
            }
        }
        
        /// <summary>
        /// 重写是否可进入，检查目标地图是否存在（模仿原版MapPortal.IsEnterable）
        /// </summary>
        public override bool IsEnterable(out string reason)
        {
            if (targetMap == null)
            {
                reason = "WULA.PocketSpace.NoTargetMap".Translate();
                return false;
            }
            reason = "";
            return true;
        }
        
        /// <summary>
        /// 重写进入事件，处理从口袋空间退出到主地图（模仿原版MapPortal.OnEntered）
        /// </summary>
        public override void OnEntered(Pawn pawn)
        {
            // 不调用 base.OnEntered，因为我们不需要原版的通知机制
            // 直接处理退出逻辑
            if (targetMap != null && pawn.Spawned)
            {
                ExitPocketSpace(pawn);
            }
        }
        
        /// <summary>
        /// 重写进入按钮文本
        /// </summary>
        public override string EnterString => "WULA.PocketSpace.ExitToMainMap".Translate();
        
        /// <summary>
        /// 重写进入按钮图标，使用原版的ViewCave图标
        /// </summary>
        protected override Texture2D EnterTex => ContentFinder<Texture2D>.Get("UI/Commands/ViewCave");
        
        /// <summary>
        /// 重写GetGizmos方法，添加穿梭机装载相关按钮
        /// </summary>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            // 获取基类的按钮（退出空间和查看地图按钮）
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            // 如果有父穿梭机，添加穿梭机相关的装载按钮
            if (parentShuttle != null)
            {
                // 查看主地图按钮
                yield return new Command_Action
                {
                    defaultLabel = "WULA.PocketSpace.ViewMainMap".Translate(),
                    defaultDesc = "WULA.PocketSpace.ViewMainMapDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewWorld"),
                    action = delegate
                    {
                        if (targetMap != null)
                        {
                            Current.Game.CurrentMap = targetMap;
                            if (parentShuttle != null && parentShuttle.Spawned)
                            {
                                Find.CameraDriver.JumpToCurrentMapLoc(parentShuttle.Position);
                                Find.Selector.Select(parentShuttle);
                            }
                            else
                            {
                                Find.CameraDriver.JumpToCurrentMapLoc(targetPos);
                            }
                        }
                    }
                };
                
                // 穿梭机装载管理按钮
                if (parentShuttle.Spawned)
                {
                    // 获取穿梭机的CompTransporter组件
                    CompTransporter transporter = parentShuttle.GetComp<CompTransporter>();
                    if (transporter != null)
                    {
                        // 添加装载按钮（模仿原版CompTransporter的功能）
                        yield return new Command_Action
                        {
                            defaultLabel = "WULA.PocketSpace.LoadShuttle".Translate(),
                            defaultDesc = "WULA.PocketSpace.LoadShuttleDesc".Translate(),
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter"),
                            action = delegate
                            {
                                OpenShuttleLoadingDialog(transporter);
                            }
                        };
                        
                        // 如果正在装载，添加取消装载按钮
                        if (transporter.LoadingInProgress)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "WULA.PocketSpace.CancelLoading".Translate(),
                                defaultDesc = "WULA.PocketSpace.CancelLoadingDesc".Translate(),
                                icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                                action = delegate
                                {
                                    transporter.CancelLoad();
                                }
                            };
                        }
                    }
                    
                    // 添加穿梭机发射按钮（如果正在装载且可以发射）
                    CompLaunchable launchable = parentShuttle.GetComp<CompLaunchable>();
                    if (launchable != null && transporter != null && !transporter.LoadingInProgress)
                    {
                        foreach (Gizmo gizmo in launchable.CompGetGizmosExtra())
                        {
                            yield return gizmo;
                        }
                    }
                }
                
                // 穿梭机状态信息按钮
                yield return new Command_Action
                {
                    defaultLabel = "WULA.PocketSpace.ShuttleStatus".Translate(),
                    defaultDesc = "WULA.PocketSpace.ShuttleStatusDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/InfoCard"),
                    action = delegate
                    {
                        ShowShuttleStatusInfo();
                    }
                };
            }
        }



        /// <summary>
        /// 单个人员退出口袋空间（简化版本，利用MapPortal功能）
        /// </summary>
        private void ExitPocketSpace(Pawn pawn)
        {
            if (targetMap == null || !pawn.Spawned) return;

            try
            {
                // 在目标地图找一个安全位置
                IntVec3 exitPos = CellFinder.RandomClosewalkCellNear(targetPos, targetMap, 3, p => p.Standable(targetMap));
                
                // 传送人员
                pawn.DeSpawn();
                GenPlace.TryPlaceThing(pawn, exitPos, targetMap, ThingPlaceMode.Near);
                
                // 切换到主地图
                if (pawn.IsColonistPlayerControlled)
                {
                    Current.Game.CurrentMap = targetMap;
                    Find.CameraDriver.JumpToCurrentMapLoc(exitPos);
                }

                Messages.Message("WULA.PocketSpace.ExitSuccess".Translate(pawn.LabelShort), MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error exiting pocket space: {ex}");
            }
        }
        
        /// <summary>
        /// 打开穿梭机装载对话框
        /// </summary>
        private void OpenShuttleLoadingDialog(CompTransporter transporter)
        {
            if (transporter == null) return;
            
            try
            {
                // 使用原版的Dialog_LoadTransporters打开装载对话框
                Find.WindowStack.Add(new Dialog_LoadTransporters(parentShuttle.Map, new List<CompTransporter> { transporter }));
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error opening shuttle loading dialog: {ex}");
                Messages.Message("WULA.PocketSpace.LoadingDialogError".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
        
        /// <summary>
        /// 显示穿梭机状态信息
        /// </summary>
        private void ShowShuttleStatusInfo()
        {
            if (parentShuttle == null) return;
            
            StringBuilder statusText = new StringBuilder();
            
            // 基本信息
            statusText.AppendLine("WULA.PocketSpace.ShuttleInfo".Translate());
            statusText.AppendLine($"• 状态: {(parentShuttle.Spawned ? "已部署" : "飞行中")}");
            
            if (parentShuttle.Spawned)
            {
                statusText.AppendLine($"• 位置: {targetMap?.Parent?.Label ?? "未知"} ({targetPos.x}, {targetPos.z})");
                
                // 燃料信息
                CompRefuelable fuel = parentShuttle.GetComp<CompRefuelable>();
                if (fuel != null)
                {
                    statusText.AppendLine($"• 燃料: {fuel.Fuel:F0}/{fuel.Props.fuelCapacity:F0}");
                }
                
                // 装载信息
                CompTransporter transporter = parentShuttle.GetComp<CompTransporter>();
                if (transporter != null)
                {
                    statusText.AppendLine($"• 载重: {transporter.MassUsage:F1}/{transporter.Props.massCapacity:F1}");
                    if (transporter.LoadingInProgress)
                    {
                        statusText.AppendLine("• 装载状态: 正在装载...");
                    }
                }
                
                // 口袋空间信息
                if (parentShuttle.pocketMapGenerated)
                {
                    statusText.AppendLine($"• 内部空间: 已初始化");
                    if (parentShuttle.innerContainer.Count > 0)
                    {
                        statusText.AppendLine($"• 内部储存: {parentShuttle.innerContainer.Count} 件物品");
                    }
                }
            }
            else
            {
                statusText.AppendLine("• 穿梭机正在飞行中，无法获取详细信息");
            }
            
            // 显示信息对话框
            Find.WindowStack.Add(new Dialog_MessageBox(statusText.ToString(), "WULA.PocketSpace.ShuttleStatus".Translate()));
        }
    }
}