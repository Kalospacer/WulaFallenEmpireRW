// CompOrbitalBombardment.cs
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompOrbitalBombardment : ThingComp
    {
        public CompProperties_OrbitalBombardment Props => (CompProperties_OrbitalBombardment)props;
        
        // 炮击状态
        private List<IntVec3> confirmedTargetCells = new List<IntVec3>();
        private HashSet<IntVec3> firedCells = new HashSet<IntVec3>();
        
        // 横向偏移状态（左右）
        private float currentLateralOffsetAngle = 0f;
        private int shotsFired = 0;
        
        // 纵向偏移状态（前后）
        private float currentLongitudinalOffset = 0f;
        private bool isForwardPhase = true;
        
        // 炮击间隔控制
        private int nextBombardmentTick = 0;
        private int currentBurstCount = 0;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 初始化偏移
            if (!respawningAfterLoad)
            {
                currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
                currentLongitudinalOffset = Props.longitudinalInitialOffset;
                nextBombardmentTick = Find.TickManager.TicksGame + Props.initialDelayTicks;
            }
            
            Log.Message($"OrbitalBombardment: Initialized with {confirmedTargetCells.Count} targets, " +
                       $"Lateral Offset: {currentLateralOffsetAngle:F1}°, " +
                       $"Longitudinal Offset: {currentLongitudinalOffset:F1}");
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (confirmedTargetCells.Count == 0 || Find.TickManager.TicksGame < nextBombardmentTick)
            {
                return;
            }
            
            CheckAndBombardTargets();
            
            // 定期状态输出
            if (Find.TickManager.TicksGame % 120 == 0 && confirmedTargetCells.Count > 0)
            {
                Log.Message($"OrbitalBombardment: {firedCells.Count}/{confirmedTargetCells.Count + firedCells.Count} targets bombarded, " +
                           $"Lateral: {currentLateralOffsetAngle:F1}°, Longitudinal: {currentLongitudinalOffset:F1}");
            }
        }
        
        private void CheckAndBombardTargets()
        {
            Vector3 currentPos = parent.DrawPos;
            
            for (int i = confirmedTargetCells.Count - 1; i >= 0; i--)
            {
                IntVec3 targetCell = confirmedTargetCells[i];
                
                if (firedCells.Contains(targetCell))
                {
                    confirmedTargetCells.RemoveAt(i);
                    continue;
                }
                
                float horizontalDistance = GetHorizontalDistance(currentPos, targetCell);
                if (horizontalDistance <= Props.range)
                {
                    if (LaunchSkyfallerAt(targetCell))
                    {
                        firedCells.Add(targetCell);
                        confirmedTargetCells.RemoveAt(i);
                        
                        // 更新所有偏移参数
                        UpdateOffsets();
                        
                        // 设置下一次炮击时间
                        UpdateNextBombardmentTick();
                        
                        if (firedCells.Count == 1)
                        {
                            Log.Message($"First orbital bombardment at {targetCell}, " +
                                       $"Lateral offset: {currentLateralOffsetAngle:F1}°, " +
                                       $"Longitudinal offset: {currentLongitudinalOffset:F1}");
                        }
                        
                        // 检查是否需要暂停（连发模式）
                        if (Props.burstMode && currentBurstCount >= Props.burstSize)
                        {
                            currentBurstCount = 0;
                            nextBombardmentTick = Find.TickManager.TicksGame + Props.burstCooldownTicks;
                            break;
                        }
                    }
                }
            }
        }
        
        // 新增：更新所有偏移参数
        private void UpdateOffsets()
        {
            shotsFired++;
            currentBurstCount++;
            
            // 更新横向偏移
            UpdateLateralOffset();
            
            // 更新纵向偏移
            UpdateLongitudinalOffset();
        }
        
        // 横向偏移逻辑（左右）
        private void UpdateLateralOffset()
        {
            switch (Props.lateralOffsetMode)
            {
                case OffsetMode.Alternating:
                    currentLateralOffsetAngle = (shotsFired % 2 == 0) ? Props.lateralOffsetDistance : -Props.lateralOffsetDistance;
                    break;
                    
                case OffsetMode.Progressive:
                    currentLateralOffsetAngle += Props.lateralAngleIncrement;
                    if (Mathf.Abs(currentLateralOffsetAngle) > Props.lateralMaxOffsetAngle)
                    {
                        currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
                    }
                    break;
                    
                case OffsetMode.Random:
                    currentLateralOffsetAngle = Random.Range(-Props.lateralMaxOffsetAngle, Props.lateralMaxOffsetAngle);
                    break;
                    
                case OffsetMode.Fixed:
                default:
                    break;
            }
            
            if (Props.lateralMaxOffsetAngle > 0)
            {
                currentLateralOffsetAngle = Mathf.Clamp(currentLateralOffsetAngle, -Props.lateralMaxOffsetAngle, Props.lateralMaxOffsetAngle);
            }
        }
        
        // 纵向偏移逻辑（前后）
        private void UpdateLongitudinalOffset()
        {
            switch (Props.longitudinalOffsetMode)
            {
                case LongitudinalOffsetMode.Alternating:
                    currentLongitudinalOffset = (shotsFired % 2 == 0) ? Props.longitudinalAlternationAmplitude : -Props.longitudinalAlternationAmplitude;
                    break;
                    
                case LongitudinalOffsetMode.Progressive:
                    if (isForwardPhase)
                    {
                        currentLongitudinalOffset += Props.longitudinalProgressionStep;
                        if (currentLongitudinalOffset >= Props.longitudinalMaxOffset)
                        {
                            isForwardPhase = false;
                        }
                    }
                    else
                    {
                        currentLongitudinalOffset -= Props.longitudinalProgressionStep;
                        if (currentLongitudinalOffset <= Props.longitudinalMinOffset)
                        {
                            isForwardPhase = true;
                        }
                    }
                    break;
                    
                case LongitudinalOffsetMode.Random:
                    currentLongitudinalOffset = Random.Range(Props.longitudinalMinOffset, Props.longitudinalMaxOffset);
                    break;
                    
                case LongitudinalOffsetMode.Sinusoidal:
                    float time = shotsFired * Props.longitudinalOscillationSpeed;
                    currentLongitudinalOffset = Mathf.Sin(time) * Props.longitudinalOscillationAmplitude;
                    break;
                    
                case LongitudinalOffsetMode.Fixed:
                default:
                    break;
            }
            
            currentLongitudinalOffset = Mathf.Clamp(currentLongitudinalOffset, Props.longitudinalMinOffset, Props.longitudinalMaxOffset);
        }
        
        // 更新下一次炮击时间
        private void UpdateNextBombardmentTick()
        {
            if (Props.burstMode && currentBurstCount < Props.burstSize)
            {
                // 连发模式中的连续射击
                nextBombardmentTick = Find.TickManager.TicksGame + Props.burstIntervalTicks;
            }
            else
            {
                // 单发模式或连发模式结束
                nextBombardmentTick = Find.TickManager.TicksGame + Props.cooldownTicks;
            }
        }
        
        // 计算包含横向和纵向偏移的目标位置
        private IntVec3 CalculateOffsetTargetPosition(IntVec3 baseTarget)
        {
            Vector3 basePos = baseTarget.ToVector3();
            Vector3 finalPos = basePos;
            
            // 应用横向偏移（左右）
            if (Mathf.Abs(currentLateralOffsetAngle) > 0.01f)
            {
                Vector3 flyDirection = GetFlyOverDirection();
                Vector3 perpendicular = Vector3.Cross(flyDirection, Vector3.up).normalized;
                float lateralOffsetDistance = Props.lateralOffsetDistance;
                Vector3 lateralOffset = perpendicular * lateralOffsetDistance * Mathf.Sin(currentLateralOffsetAngle * Mathf.Deg2Rad);
                finalPos += lateralOffset;
            }
            
            // 应用纵向偏移（前后）
            if (Mathf.Abs(currentLongitudinalOffset) > 0.01f)
            {
                Vector3 flyDirection = GetFlyOverDirection();
                Vector3 longitudinalOffset = flyDirection * currentLongitudinalOffset;
                finalPos += longitudinalOffset;
            }
            
            return finalPos.ToIntVec3();
        }
        
        private Vector3 GetFlyOverDirection()
        {
            FlyOver flyOver = parent as FlyOver;
            if (flyOver != null)
            {
                return flyOver.MovementDirection;
            }
            return Vector3.forward;
        }
        
        private float GetHorizontalDistance(Vector3 fromPos, IntVec3 toCell)
        {
            Vector2 fromPos2D = new Vector2(fromPos.x, fromPos.z);
            Vector2 toPos2D = new Vector2(toCell.x, toCell.z);
            return Vector2.Distance(fromPos2D, toPos2D);
        }
        
        private bool LaunchSkyfallerAt(IntVec3 targetCell)
        {
            if (Props.skyfallerDef == null)
            {
                Log.Error("No skyfaller defined for orbital bombardment");
                return false;
            }
            
            try
            {
                // 计算偏移后的目标位置
                IntVec3 offsetTarget = CalculateOffsetTargetPosition(targetCell);
                
                // 确保目标位置在地图范围内
                if (!offsetTarget.InBounds(parent.Map))
                {
                    Log.Warning($"OrbitalBombardment: Offset target position {offsetTarget} is out of bounds, using original target {targetCell}");
                    offsetTarget = targetCell;
                }
                
                // 创建 Skyfaller
                Skyfaller skyfaller = SkyfallerMaker.SpawnSkyfaller(
                    Props.skyfallerDef,
                    offsetTarget,
                    parent.Map
                );
                
                if (skyfaller != null)
                {
                    // 设置发射者信息（如果需要）
                    Thing launcher = GetLauncher();
                    if (launcher != null)
                    {
                        // 这里可以设置 Skyfaller 的发射者信息
                        // 具体取决于 Skyfaller 的实现
                    }
                    
                    // 播放炮击特效
                    if (Props.spawnBombardmentEffect)
                    {
                        CreateBombardmentEffect(offsetTarget);
                    }
                    
                    Log.Message($"OrbitalBombardment: Launched {Props.skyfallerDef.defName} at {offsetTarget}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error launching orbital bombardment skyfaller: {ex}");
            }
            
            return false;
        }
        
        // 炮击特效
        private void CreateBombardmentEffect(IntVec3 targetPos)
        {
            if (Props.bombardmentEffectDef != null)
            {
                MoteMaker.MakeStaticMote(
                    targetPos.ToVector3Shifted(), 
                    parent.Map, 
                    Props.bombardmentEffectDef, 
                    Props.bombardmentEffectScale
                );
            }
        }
        
        private Thing GetLauncher()
        {
            FlyOver flyOver = parent as FlyOver;
            // 如果需要，可以返回发射者信息
            return parent;
        }
        
        public void SetConfirmedTargets(List<IntVec3> targets)
        {
            confirmedTargetCells.Clear();
            firedCells.Clear();
            shotsFired = 0;
            currentBurstCount = 0;
            currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
            currentLongitudinalOffset = Props.longitudinalInitialOffset;
            isForwardPhase = true;
            
            confirmedTargetCells.AddRange(targets);
            
            // 设置首次炮击时间
            nextBombardmentTick = Find.TickManager.TicksGame + Props.initialDelayTicks;
            
            Log.Message($"OrbitalBombardment: Set {confirmedTargetCells.Count} targets, " +
                       $"Lateral Mode: {Props.lateralOffsetMode}, " +
                       $"Longitudinal Mode: {Props.longitudinalOffsetMode}, " +
                       $"Initial Delay: {Props.initialDelayTicks} ticks");
            
            if (confirmedTargetCells.Count > 0)
            {
                Log.Message($"First target: {confirmedTargetCells[0]}, Last target: {confirmedTargetCells[confirmedTargetCells.Count - 1]}");
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Collections.Look(ref confirmedTargetCells, "confirmedTargetCells", LookMode.Value);
            Scribe_Collections.Look(ref firedCells, "firedCells", LookMode.Value);
            Scribe_Values.Look(ref currentLateralOffsetAngle, "currentLateralOffsetAngle", Props.lateralInitialOffsetAngle);
            Scribe_Values.Look(ref currentLongitudinalOffset, "currentLongitudinalOffset", Props.longitudinalInitialOffset);
            Scribe_Values.Look(ref shotsFired, "shotsFired", 0);
            Scribe_Values.Look(ref isForwardPhase, "isForwardPhase", true);
            Scribe_Values.Look(ref nextBombardmentTick, "nextBombardmentTick", 0);
            Scribe_Values.Look(ref currentBurstCount, "currentBurstCount", 0);
        }
        
        // 调试方法
        public void DebugBombardmentStatus()
        {
            Log.Message($"OrbitalBombardment Status:");
            Log.Message($"  Lateral - Angle: {currentLateralOffsetAngle:F1}°, Mode: {Props.lateralOffsetMode}");
            Log.Message($"  Longitudinal - Offset: {currentLongitudinalOffset:F1}, Mode: {Props.longitudinalOffsetMode}");
            Log.Message($"  Shots Fired: {shotsFired}, Forward Phase: {isForwardPhase}");
            Log.Message($"  Next Bombardment: {nextBombardmentTick}, Current Burst: {currentBurstCount}/{Props.burstSize}");
            Log.Message($"  Targets: {confirmedTargetCells.Count} remaining, {firedCells.Count} completed");
        }
        
        // 获取剩余目标数量
        public int GetRemainingTargets()
        {
            return confirmedTargetCells.Count;
        }
        
        // 获取总进度
        public float GetCompletionProgress()
        {
            int totalTargets = confirmedTargetCells.Count + firedCells.Count;
            if (totalTargets == 0) return 1f;
            return (float)firedCells.Count / totalTargets;
        }
    }
    
    public class CompProperties_OrbitalBombardment : CompProperties
    {
        public ThingDef skyfallerDef;           // Skyfaller 定义
        public float range = 25f;               // 炮击范围
        
        // 炮击时序控制
        public int initialDelayTicks = 60;      // 初始延迟（游戏刻）
        public int cooldownTicks = 30;          // 冷却时间（游戏刻）
        public bool burstMode = false;          // 是否使用连发模式
        public int burstSize = 3;               // 连发数量
        public int burstIntervalTicks = 10;     // 连发间隔（游戏刻）
        public int burstCooldownTicks = 60;     // 连发后冷却（游戏刻）
        
        // 横向偏移配置（左右）
        public float lateralOffsetDistance = 2f;
        public float lateralInitialOffsetAngle = 0f;
        public float lateralMaxOffsetAngle = 45f;
        public float lateralAngleIncrement = 5f;
        public OffsetMode lateralOffsetMode = OffsetMode.Alternating;
        
        // 纵向偏移配置（前后）
        public float longitudinalInitialOffset = 0f;
        public float longitudinalMinOffset = -2f;
        public float longitudinalMaxOffset = 2f;
        public LongitudinalOffsetMode longitudinalOffsetMode = LongitudinalOffsetMode.Alternating;
        
        // 正弦波模式参数
        public float longitudinalOscillationSpeed = 0.5f;
        public float longitudinalOscillationAmplitude = 1f;
        
        // 交替模式参数
        public float longitudinalAlternationAmplitude = 1f;
        
        // 渐进模式参数
        public float longitudinalProgressionStep = 0.1f;
        
        // 视觉效果和音效
        public bool spawnBombardmentEffect = true;
        public ThingDef bombardmentEffectDef;
        public float bombardmentEffectScale = 1f;
        public SoundDef bombardmentSound;
        
        public CompProperties_OrbitalBombardment()
        {
            compClass = typeof(CompOrbitalBombardment);
        }
    }
}
