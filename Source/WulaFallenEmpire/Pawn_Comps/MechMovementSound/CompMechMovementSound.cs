// CompMechMovementSound_Fixed.cs
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompMechMovementSound : ThingComp
    {
        public CompProperties_MechMovementSound Props => (CompProperties_MechMovementSound)props;
        
        // 核心状态
        private Sustainer soundSustainer;
        private bool isPlaying = false;
        private Vector3 lastPosition = Vector3.zero;
        private float currentSpeed = 0f;
        private int ticksSinceLastMovement = 0;
        private bool wasMovingLastTick = false;
        
        // 缓存引用
        private Pawn mechPawn;
        private CompPowerTrader powerComp;
        private CompMechPilotHolder pilotComp;
        
        // 状态平滑
        private const int MOVEMENT_CHECK_INTERVAL = 3; // 每10ticks检查一次移动
        private const int STOP_DELAY_TICKS = 1; // 停止后延迟30ticks再停止音效
        private const float SPEED_SMOOTHING = 0.2f; // 速度平滑系数
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            mechPawn = parent as Pawn;
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            powerComp = parent.TryGetComp<CompPowerTrader>();
            pilotComp = parent.TryGetComp<CompMechPilotHolder>();
            
            if (mechPawn != null && mechPawn.Spawned)
            {
                lastPosition = GetCurrentPositionSafe();
            }
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            // 每帧都更新位置，但减少移动状态检查频率
            if (mechPawn != null && mechPawn.Spawned)
            {
                UpdatePosition();
            }
            
            // 每10ticks检查一次移动状态
            if (Find.TickManager.TicksGame % MOVEMENT_CHECK_INTERVAL == 0)
            {
                UpdateMovementState();
            }
            
            // 每帧维持音效
            MaintainSound();
        }
        
        // 安全的获取当前位置
        private Vector3 GetCurrentPositionSafe()
        {
            try
            {
                if (mechPawn == null || !mechPawn.Spawned)
                    return mechPawn?.Position.ToVector3Shifted() ?? Vector3.zero;
                
                // 优先使用DrawPos，如果不可用则使用网格位置
                if (mechPawn.Drawer != null)
                {
                    return mechPawn.DrawPos;
                }
                return mechPawn.Position.ToVector3Shifted();
            }
            catch
            {
                return mechPawn?.Position.ToVector3Shifted() ?? Vector3.zero;
            }
        }
        
        // 更新位置（每帧）
        private void UpdatePosition()
        {
            Vector3 currentPos = GetCurrentPositionSafe();
            
            // 计算当前帧的速度（使用真实时间）
            float deltaTime = Time.deltaTime;
            if (deltaTime > 0)
            {
                float distance = Vector3.Distance(currentPos, lastPosition);
                float rawSpeed = distance / deltaTime;
                
                // 应用平滑过滤
                currentSpeed = Mathf.Lerp(currentSpeed, rawSpeed, SPEED_SMOOTHING);
            }
            
            lastPosition = currentPos;
        }
        
        // 更新移动状态（低频检查）
        private void UpdateMovementState()
        {
            if (!ShouldProcess())
            {
                ticksSinceLastMovement++;
                if (isPlaying && ticksSinceLastMovement > STOP_DELAY_TICKS)
                {
                    StopSound();
                }
                return;
            }
            
            // 检查是否在移动
            bool isMoving = CheckIfMoving();
            
            // 更新移动状态
            if (isMoving)
            {
                ticksSinceLastMovement = 0;
                
                if (!isPlaying)
                {
                    StartSound();
                }
            }
            else
            {
                ticksSinceLastMovement++;
                
                // 延迟停止，避免频繁启停
                if (isPlaying && ticksSinceLastMovement > STOP_DELAY_TICKS)
                {
                    StopSound();
                }
            }
            
            wasMovingLastTick = isMoving;
        }
        
        // 检查是否应该处理音效
        private bool ShouldProcess()
        {
            if (mechPawn == null || Props.movementSound == null)
                return false;
            
            // 基础状态检查
            if (!mechPawn.Spawned || mechPawn.Dead || mechPawn.Downed || mechPawn.InMentalState)
                return false;
            
            // 条件检查
            if (Props.requirePower && powerComp != null && !powerComp.PowerOn)
                return false;
            
            if (Props.requirePilot && pilotComp != null && !pilotComp.HasPilots)
                return false;
            
            return true;
        }
        
        // 综合判断是否在移动
        private bool CheckIfMoving()
        {
            // 方法1：速度阈值
            if (currentSpeed > Props.minMovementSpeed)
                return true;
            
            // 方法2：检查寻路器
            if (mechPawn.pather?.Moving ?? false)
                return true;
            
            // 方法3：检查当前任务
            var job = mechPawn.CurJob;
            if (job != null)
            {
                if (job.def == JobDefOf.Goto ||
                    job.def == JobDefOf.GotoWander ||
                    job.def == JobDefOf.Flee ||
                    job.def == JobDefOf.Follow)
                    return true;
            }
            
            return false;
        }
        
        // 维持音效
        private void MaintainSound()
        {
            if (soundSustainer != null && isPlaying)
            {
                try
                {
                    // 更新音效位置
                    if (mechPawn != null && mechPawn.Spawned)
                    {
                        var map = mechPawn.Map;
                        if (map != null)
                        {
                            // 创建一个新的SoundInfo来更新位置
                            SoundInfo soundInfo = SoundInfo.InMap(mechPawn, MaintenanceType.PerTick);
                            soundSustainer?.SustainerUpdate();
                        }
                    }
                    
                    // 维持音效
                    soundSustainer.Maintain();
                }
                catch
                {
                    // 如果sustainer失效，重置状态
                    soundSustainer = null;
                    isPlaying = false;
                }
            }
        }
        
        // 开始音效
        private void StartSound()
        {
            if (Props.movementSound == null || soundSustainer != null)
                return;
            
            try
            {
                // 创建音效信息
                SoundInfo soundInfo = SoundInfo.InMap(mechPawn, MaintenanceType.PerTick);
                
                // 使用TrySpawnSustainer
                soundSustainer = Props.movementSound.TrySpawnSustainer(soundInfo);
                
                if (soundSustainer != null)
                {
                    isPlaying = true;
                }
                else
                {
                    Log.Warning($"[WULA] Failed to create sustainer for {Props.movementSound.defName}");
                    isPlaying = false;
                }
            }
            catch
            {
                soundSustainer = null;
                isPlaying = false;
            }
        }
        
        // 停止音效
        private void StopSound()
        {
            if (soundSustainer != null)
            {
                try
                {
                    // 先检查sustainer是否有效
                    if (!soundSustainer.Ended)
                    {
                        soundSustainer.End();
                    }
                }
                finally
                {
                    soundSustainer = null;
                    isPlaying = false;
                }
            }
        }
        
        // 事件处理
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            StopSound();
        }
        
        public void PostDeSpawn(Map map)
        {
            base.PostDeSpawn(map);
            StopSound();
        }
        
        public override void Notify_Downed()
        {
            base.Notify_Downed();
            StopSound();
        }
        
        // 序列化
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref lastPosition, "lastPosition", Vector3.zero);
            Scribe_Values.Look(ref currentSpeed, "currentSpeed", 0f);
            Scribe_Values.Look(ref ticksSinceLastMovement, "ticksSinceLastMovement", 0);
            Scribe_Values.Look(ref wasMovingLastTick, "wasMovingLastTick", false);
            
            // 加载后重新初始化
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                soundSustainer = null;
                isPlaying = false;
            }
        }
        
        // 调试信息
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            if (DebugSettings.ShowDevGizmos && mechPawn != null && mechPawn.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Sound Debug",
                    defaultDesc = GetDebugInfo(),
                    action = () =>
                    {
                        // 切换声音状态
                        if (soundSustainer == null)
                        {
                            StartSound();
                            Messages.Message("Sound started", mechPawn, MessageTypeDefOf.NeutralEvent);
                        }
                        else
                        {
                            StopSound();
                            Messages.Message("Sound stopped", mechPawn, MessageTypeDefOf.NeutralEvent);
                        }
                    }
                };
            }
        }
        
        private string GetDebugInfo()
        {
            return $"Movement Sound Debug:\n" +
                   $"  Playing: {isPlaying}\n" +
                   $"  Speed: {currentSpeed:F2} (min: {Props.minMovementSpeed})\n" +
                   $"  Ticks since move: {ticksSinceLastMovement}\n" +
                   $"  Sustainer: {soundSustainer != null}\n" +
                   $"  Was moving: {wasMovingLastTick}\n" +
                   $"  Pawn pathing: {mechPawn.pather?.Moving ?? false}";
        }
    }
}
