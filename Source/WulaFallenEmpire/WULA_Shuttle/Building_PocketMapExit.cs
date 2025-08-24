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
    }
}