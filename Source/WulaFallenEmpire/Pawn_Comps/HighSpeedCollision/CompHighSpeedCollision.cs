using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 高速移动撞击组件
    /// </summary>
    public class CompHighSpeedCollision : ThingComp
    {
        // === 运行时状态 ===
        private enum SpeedStage
        {
            Stage0, // 0阶段：不移动
            Stage1, // 1阶段：低速碰撞
            Stage2  // 2阶段：高速击飞
        }
        
        private SpeedStage currentStage = SpeedStage.Stage0;
        private int stageTransitionCooldown = 0;
        
        // 用于计算速度的帧历史
        private Queue<float> speedHistory = new Queue<float>();
        private IntVec3 lastPosition = IntVec3.Invalid;
        private int lastPositionTick = -1;
        
        // 已处理的敌人记录（避免同一帧重复处理）
        private HashSet<Pawn> processedPawnsThisTick = new HashSet<Pawn>();
        
        // === 缓存 ===
        private CellRect collisionAreaCache = default;
        private int lastAreaRecalculationTick = -1;
        
        public CompProperties_HighSpeedCollision Props => (CompProperties_HighSpeedCollision)props;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 初始化速度历史
            speedHistory.Clear();
            for (int i = 0; i < Props.speedHistoryFrameCount; i++)
            {
                speedHistory.Enqueue(0f);
            }
            
            lastPosition = parent.Position;
            lastPositionTick = Find.TickManager.TicksGame;
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (!parent.Spawned || parent.Destroyed)
                return;
            
            Pawn pawn = parent as Pawn;
            if (pawn == null || pawn.Dead || pawn.Downed)
                return;
            
            // 检查是否死亡或不能移动
            if (!CanMove(pawn))
            {
                ResetToStage0();
                return;
            }
            
            // 每帧更新
            ProcessFrame(pawn);
        }
        
        /// <summary>
        /// 处理每帧逻辑
        /// </summary>
        private void ProcessFrame(Pawn pawn)
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // 1. 计算当前速度
            float currentSpeed = CalculateCurrentSpeed(pawn, currentTick);
            
            // 2. 更新速度历史
            UpdateSpeedHistory(currentSpeed);
            
            // 3. 计算平均速度
            float averageSpeed = GetAverageSpeed();
            
            // 4. 确定阶段
            DetermineSpeedStage(averageSpeed);
            
            // 5. 根据阶段应用效果
            ApplyStageEffects(pawn);
            
            // 6. 清理每帧记录
            processedPawnsThisTick.Clear();
            
            // 7. 更新冷却
            if (stageTransitionCooldown > 0)
                stageTransitionCooldown--;
            
            // 8. 调试绘制
            if (Props.enableDebugVisuals && DebugSettings.godMode)
                DrawDebugVisuals(pawn);
        }
        
        /// <summary>
        /// 计算当前速度（每秒格数）
        /// </summary>
        private float CalculateCurrentSpeed(Pawn pawn, int currentTick)
        {
            // 如果没有上次位置记录，无法计算速度
            if (lastPositionTick < 0 || lastPosition == IntVec3.Invalid)
            {
                lastPosition = pawn.Position;
                lastPositionTick = currentTick;
                return 0f;
            }
            
            // 计算时间差（秒）
            float timeDelta = (currentTick - lastPositionTick) / 60f;
            if (timeDelta <= 0f)
                return 0f;
            
            // 计算距离（格数）
            float distance = pawn.Position.DistanceTo(lastPosition);
            
            // 计算速度（格/秒）
            float speed = distance / timeDelta;
            
            // 更新记录
            lastPosition = pawn.Position;
            lastPositionTick = currentTick;
            
            return speed;
        }
        
        /// <summary>
        /// 更新速度历史
        /// </summary>
        private void UpdateSpeedHistory(float currentSpeed)
        {
            speedHistory.Enqueue(currentSpeed);
            while (speedHistory.Count > Props.speedHistoryFrameCount)
            {
                speedHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// 获取平均速度
        /// </summary>
        private float GetAverageSpeed()
        {
            if (speedHistory.Count == 0)
                return 0f;
            
            float sum = 0f;
            foreach (float speed in speedHistory)
            {
                sum += speed;
            }
            
            return sum / speedHistory.Count;
        }
        
        /// <summary>
        /// 确定速度阶段
        /// </summary>
        private void DetermineSpeedStage(float averageSpeed)
        {
            // 如果有冷却，保持当前阶段
            if (stageTransitionCooldown > 0)
                return;
            
            SpeedStage newStage;
            
            if (averageSpeed <= Props.minSpeedForStage1)
            {
                newStage = SpeedStage.Stage0;
            }
            else if (averageSpeed >= Props.minSpeedForStage2)
            {
                newStage = SpeedStage.Stage2;
            }
            else
            {
                newStage = SpeedStage.Stage1;
            }
            
            // 阶段变化时设置冷却
            if (newStage != currentStage)
            {
                currentStage = newStage;
                stageTransitionCooldown = Props.stageTransitionCooldownTicks;
                
                if (Props.enableDebugLogging)
                {
                    Log.Message($"[HighSpeedCollision] {parent.Label} transitioned to Stage {(int)currentStage} " +
                               $"at speed {averageSpeed:F2} cells/sec");
                }
            }
        }
        
        /// <summary>
        /// 应用阶段效果
        /// </summary>
        private void ApplyStageEffects(Pawn pawn)
        {
            if (currentStage == SpeedStage.Stage0)
                return;
            
            // 获取碰撞区域内的所有敌人
            List<Pawn> enemiesInArea = GetEnemiesInCollisionArea(pawn);
            
            foreach (Pawn enemy in enemiesInArea)
            {
                if (enemy == null || enemy.Destroyed || enemy.Dead || processedPawnsThisTick.Contains(enemy))
                    continue;
                
                switch (currentStage)
                {
                    case SpeedStage.Stage1:
                        ApplyStage1Effects(pawn, enemy);
                        break;
                    case SpeedStage.Stage2:
                        ApplyStage2Effects(pawn, enemy);
                        break;
                }
                
                processedPawnsThisTick.Add(enemy);
            }
        }
        
        /// <summary>
        /// 应用阶段1效果（伤害+hediff）
        /// </summary>
        private void ApplyStage1Effects(Pawn attacker, Pawn target)
        {
            // 检查目标是否已有hediff
            bool alreadyHasHediff = target.health.hediffSet.HasHediff(Props.stage1Hediff);
            
            // 如果已有hediff，不造成伤害
            if (alreadyHasHediff && Props.stage1HediffPreventsDamage)
                return;
            
            // 造成伤害
            if (Props.stage1DamageAmount > 0f)
            {
                ApplyDamage(attacker, target, Props.stage1DamageAmount, Props.stage1DamageDef);
            }
            
            // 应用hediff
            if (Props.stage1Hediff != null)
            {
                Hediff hediff = HediffMaker.MakeHediff(Props.stage1Hediff, target);
                if (Props.stage1HediffDurationTicks > 0)
                {
                    hediff.Severity = 1f;
                    hediff.TryGetComp<HediffComp_Disappears>()?.CompPostMake();
                }
                target.health.AddHediff(hediff);
            }
            
            // 播放效果
            PlayStage1Effects(attacker, target);
            
            if (Props.enableDebugLogging)
            {
                Log.Message($"[HighSpeedCollision] Stage1: {attacker.Label} -> {target.Label}, " +
                           $"Damage: {Props.stage1DamageAmount}, Hediff: {Props.stage1Hediff?.defName}");
            }
        }
        
        /// <summary>
        /// 应用阶段2效果（伤害+击飞）
        /// </summary>
        private void ApplyStage2Effects(Pawn attacker, Pawn target)
        {
            // 造成伤害
            if (Props.stage2DamageAmount > 0f)
            {
                ApplyDamage(attacker, target, Props.stage2DamageAmount, Props.stage2DamageDef);
            }
            
            // 执行击飞
            PerformKnockback(attacker, target);
            
            // 播放效果
            PlayStage2Effects(attacker, target);
            
            if (Props.enableDebugLogging)
            {
                Log.Message($"[HighSpeedCollision] Stage2: {attacker.Label} -> {target.Label}, " +
                           $"Damage: {Props.stage2DamageAmount}, Knockback");
            }
        }
        
        /// <summary>
        /// 执行击飞（参考CompAbilityEffect_FanShapedStunKnockback）
        /// </summary>
        private void PerformKnockback(Pawn attacker, Pawn target)
        {
            if (target == null || target.Destroyed || target.Dead || attacker.Map == null)
                return;
            
            // 计算击飞方向（从攻击者指向目标）
            IntVec3 knockbackDirection = CalculateKnockbackDirection(attacker, target.Position);
            
            // 寻找击飞位置
            IntVec3 knockbackDestination = FindKnockbackDestination(attacker, target, knockbackDirection);
            
            // 如果找到有效位置，执行击飞
            if (knockbackDestination.IsValid && knockbackDestination != target.Position)
            {
                CreateKnockbackFlyer(attacker, target, knockbackDestination);
            }
        }
        
        /// <summary>
        /// 计算击飞方向
        /// </summary>
        private IntVec3 CalculateKnockbackDirection(Pawn attacker, IntVec3 targetPosition)
        {
            IntVec3 direction = targetPosition - attacker.Position;
            
            // 标准化方向
            if (direction.x != 0 || direction.z != 0)
            {
                if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                {
                    return new IntVec3(Mathf.Sign(direction.x) > 0 ? 1 : -1, 0, 0);
                }
                else
                {
                    return new IntVec3(0, 0, Mathf.Sign(direction.z) > 0 ? 1 : -1);
                }
            }
            
            // 如果攻击者和目标在同一位置，使用随机方向
            return new IntVec3(Rand.Value > 0.5f ? 1 : -1, 0, 0);
        }
        
        /// <summary>
        /// 寻找击飞位置
        /// </summary>
        private IntVec3 FindKnockbackDestination(Pawn attacker, Pawn target, IntVec3 direction)
        {
            Map map = attacker.Map;
            IntVec3 currentPos = target.Position;
            
            // 从最大距离开始向回找
            for (int distance = Props.stage2KnockbackDistance; distance >= 1; distance--)
            {
                IntVec3 testPos = currentPos + (direction * distance);
                
                if (!IsValidKnockbackDestination(testPos, map, target, attacker))
                    continue;
                
                return testPos;
            }
            
            return currentPos;
        }
        
        /// <summary>
        /// 检查击飞位置是否有效
        /// </summary>
        private bool IsValidKnockbackDestination(IntVec3 destination, Map map, Pawn victim, Pawn attacker)
        {
            if (!destination.IsValid || !destination.InBounds(map))
                return false;
            
            if (!destination.Standable(map))
                return false;
            
            // 检查是否有其他pawn
            Pawn existingPawn = destination.GetFirstPawn(map);
            if (existingPawn != null && existingPawn != victim)
                return false;
            
            // 检查视线
            if (Props.requireLineOfSightForKnockback && !GenSight.LineOfSight(victim.Position, destination, map))
                return false;
            
            return true;
        }
        
        /// <summary>
        /// 创建击飞飞行器
        /// </summary>
        private void CreateKnockbackFlyer(Pawn attacker, Pawn target, IntVec3 destination)
        {
            try
            {
                Map map = attacker.Map;
                
                // 使用自定义飞行器或默认飞行器
                ThingDef flyerDef = Props.knockbackFlyerDef ?? ThingDefOf.PawnFlyer;
                
                // 创建飞行器
                PawnFlyer flyer = PawnFlyer.MakeFlyer(
                    flyerDef,
                    target,
                    destination,
                    Props.flightEffecterDef,
                    Props.landingSound,
                    false,
                    null,
                    null,
                    new LocalTargetInfo(destination)
                );
                
                if (flyer != null)
                {
                    GenSpawn.Spawn(flyer, destination, map);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[HighSpeedCollision] Exception creating PawnFlyer: {ex}");
            }
        }
        
        /// <summary>
        /// 应用伤害
        /// </summary>
        private void ApplyDamage(Pawn attacker, Pawn target, float amount, DamageDef damageDef)
        {
            if (amount <= 0f || damageDef == null)
                return;
            
            DamageInfo damageInfo = new DamageInfo(
                damageDef,
                amount,
                Props.armorPenetration,
                -1f,
                attacker,
                null
            );
            
            target.TakeDamage(damageInfo);
        }
        
        /// <summary>
        /// 播放阶段1效果
        /// </summary>
        private void PlayStage1Effects(Pawn attacker, Pawn target)
        {
            if (Props.stage1Effecter != null && attacker.Map != null)
            {
                Effecter effect = Props.stage1Effecter.Spawn();
                effect.Trigger(new TargetInfo(attacker.Position, attacker.Map), 
                             new TargetInfo(target.Position, attacker.Map));
                effect.Cleanup();
            }
            
            if (Props.stage1Sound != null && attacker.Map != null)
            {
                Props.stage1Sound.PlayOneShot(new TargetInfo(target.Position, attacker.Map));
            }
        }
        
        /// <summary>
        /// 播放阶段2效果
        /// </summary>
        private void PlayStage2Effects(Pawn attacker, Pawn target)
        {
            if (Props.stage2Effecter != null && attacker.Map != null)
            {
                Effecter effect = Props.stage2Effecter.Spawn();
                effect.Trigger(new TargetInfo(attacker.Position, attacker.Map), 
                             new TargetInfo(target.Position, attacker.Map));
                effect.Cleanup();
            }
            
            if (Props.stage2Sound != null && attacker.Map != null)
            {
                Props.stage2Sound.PlayOneShot(new TargetInfo(target.Position, attacker.Map));
            }
        }
        
        /// <summary>
        /// 获取碰撞区域内的所有敌人
        /// </summary>
        private List<Pawn> GetEnemiesInCollisionArea(Pawn pawn)
        {
            List<Pawn> enemies = new List<Pawn>();
            
            // 获取碰撞区域
            CellRect collisionArea = GetCollisionArea(pawn);
            
            // 检查区域内的每个单元格
            foreach (IntVec3 cell in collisionArea)
            {
                if (!cell.InBounds(pawn.Map))
                    continue;
                
                // 获取单元格内的所有pawn
                List<Thing> things = cell.GetThingList(pawn.Map);
                foreach (Thing thing in things)
                {
                    if (thing is Pawn otherPawn && otherPawn != pawn)
                    {
                        // 检查是否为敌人
                        if (IsValidTarget(pawn, otherPawn))
                        {
                            enemies.Add(otherPawn);
                        }
                    }
                }
            }
            
            return enemies;
        }
        
        /// <summary>
        /// 获取碰撞区域
        /// </summary>
        private CellRect GetCollisionArea(Pawn pawn)
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // 每10帧重新计算一次区域，或当位置变化时
            if (currentTick - lastAreaRecalculationTick > 10 || 
                pawn.Position != collisionAreaCache.CenterCell)
            {
                int radius = Props.collisionAreaRadius;
                IntVec3 center = pawn.Position;
                
                collisionAreaCache = new CellRect(
                    center.x - radius,
                    center.z - radius,
                    radius * 2 + 1,
                    radius * 2 + 1
                );
                
                collisionAreaCache.ClipInsideMap(pawn.Map);
                lastAreaRecalculationTick = currentTick;
            }
            
            return collisionAreaCache;
        }
        
        /// <summary>
        /// 检查是否是有效目标
        /// </summary>
        private bool IsValidTarget(Pawn attacker, Pawn target)
        {
            if (target == null || target.Destroyed || target.Dead)
                return false;
            
            // 检查是否为敌人
            if (Props.onlyAffectEnemies && !target.HostileTo(attacker))
                return false;
            
            // 检查是否排除友方
            if (Props.excludeAlliedPawns && target.Faction == attacker.Faction)
                return false;
            
            // 检查是否排除中立
            if (Props.excludeNeutralPawns && !target.HostileTo(attacker))
                return false;
            
            return true;
        }
        
        /// <summary>
        /// 检查Pawn是否可以移动
        /// </summary>
        private bool CanMove(Pawn pawn)
        {
            if (pawn.Downed || pawn.Dead || pawn.InMentalState)
                return false;
            
            if (pawn.stances?.stunner?.Stunned ?? false)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// 重置到阶段0
        /// </summary>
        private void ResetToStage0()
        {
            currentStage = SpeedStage.Stage0;
            
            // 清空速度历史
            speedHistory.Clear();
            for (int i = 0; i < Props.speedHistoryFrameCount; i++)
            {
                speedHistory.Enqueue(0f);
            }
        }
        
        /// <summary>
        /// 绘制调试视觉效果
        /// </summary>
        private void DrawDebugVisuals(Pawn pawn)
        {
            if (!pawn.Spawned)
                return;
            
            // 绘制碰撞区域
            CellRect area = GetCollisionArea(pawn);
            GenDraw.DrawFieldEdges(area.Cells.ToList(), GetStageColor(currentStage));
            
            // 绘制速度指示器
            float averageSpeed = GetAverageSpeed();
            string speedText = $"Speed: {averageSpeed:F1} cells/sec\nStage: {(int)currentStage}";
            
            Vector3 drawPos = pawn.DrawPos + new Vector3(0, 0, 1f);
            GenMapUI.DrawText(drawPos, speedText, GetStageColor(currentStage));
        }

        /// <summary>
        /// 获取阶段颜色
        /// </summary>
        private Color GetStageColor(SpeedStage stage)
        {
            switch (stage)
            {
                case SpeedStage.Stage0: return Color.gray;
                case SpeedStage.Stage1: return Color.yellow;
                case SpeedStage.Stage2: return Color.red;
                default: return Color.white;
            }
        }
        
        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            float averageSpeed = GetAverageSpeed();
            
            return $"HighSpeedCollision Debug Info:\n" +
                   $"Current Stage: {(int)currentStage}\n" +
                   $"Average Speed: {averageSpeed:F2} cells/sec\n" +
                   $"Stage 1 Threshold: {Props.minSpeedForStage1:F2}\n" +
                   $"Stage 2 Threshold: {Props.minSpeedForStage2:F2}\n" +
                   $"Speed History: {speedHistory.Count} frames\n" +
                   $"Stage Cooldown: {stageTransitionCooldown} ticks";
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentStage, "currentStage", SpeedStage.Stage0);
            Scribe_Values.Look(ref stageTransitionCooldown, "stageTransitionCooldown", 0);
            Scribe_Values.Look(ref lastPosition, "lastPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref lastPositionTick, "lastPositionTick", -1);
            
            // 保存速度历史
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                List<float> speedList = speedHistory.ToList();
                Scribe_Collections.Look(ref speedList, "speedHistory", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<float> speedList = null;
                Scribe_Collections.Look(ref speedList, "speedHistory", LookMode.Value);
                
                speedHistory.Clear();
                if (speedList != null)
                {
                    foreach (float speed in speedList)
                    {
                        speedHistory.Enqueue(speed);
                    }
                }
                
                // 确保有足够的历史数据
                while (speedHistory.Count < Props.speedHistoryFrameCount)
                {
                    speedHistory.Enqueue(0f);
                }
            }
        }
    }
    
    /// <summary>
    /// 高速移动撞击组件属性
    /// </summary>
    public class CompProperties_HighSpeedCollision : CompProperties
    {
        // === 速度阈值配置 ===
        
        /// <summary>
        /// 进入阶段1所需的最小速度（格/秒）
        /// </summary>
        public float minSpeedForStage1 = 3f;
        
        /// <summary>
        /// 进入阶段2所需的最小速度（格/秒）
        /// </summary>
        public float minSpeedForStage2 = 6f;
        
        /// <summary>
        /// 速度历史帧数（用于计算平均速度）
        /// </summary>
        public int speedHistoryFrameCount = 10;
        
        /// <summary>
        /// 阶段转换冷却时间（tick）
        /// </summary>
        public int stageTransitionCooldownTicks = 5;
        
        // === 碰撞区域配置 ===
        
        /// <summary>
        /// 碰撞区域半径（以pawn为中心的正方形）
        /// </summary>
        public int collisionAreaRadius = 1;
        
        // === 目标过滤 ===
        
        /// <summary>
        /// 只影响敌人
        /// </summary>
        public bool onlyAffectEnemies = true;
        
        /// <summary>
        /// 排除友方单位
        /// </summary>
        public bool excludeAlliedPawns = true;
        
        /// <summary>
        /// 排除中立单位
        /// </summary>
        public bool excludeNeutralPawns = false;
        
        // === 阶段1效果配置 ===
        
        /// <summary>
        /// 阶段1伤害类型
        /// </summary>
        public DamageDef stage1DamageDef = DamageDefOf.Blunt;
        
        /// <summary>
        /// 阶段1伤害量
        /// </summary>
        public float stage1DamageAmount = 5f;
        
        /// <summary>
        /// 阶段1护甲穿透
        /// </summary>
        public float armorPenetration = 0f;
        
        /// <summary>
        /// 阶段1应用的hediff
        /// </summary>
        public HediffDef stage1Hediff;
        
        /// <summary>
        /// 阶段1hediff持续时间（tick）
        /// </summary>
        public int stage1HediffDurationTicks = 60;
        
        /// <summary>
        /// 拥有hediff的目标是否免疫伤害
        /// </summary>
        public bool stage1HediffPreventsDamage = true;
        
        /// <summary>
        /// 阶段1效果器
        /// </summary>
        public EffecterDef stage1Effecter;
        
        /// <summary>
        /// 阶段1音效
        /// </summary>
        public SoundDef stage1Sound;
        
        // === 阶段2效果配置 ===
        
        /// <summary>
        /// 阶段2伤害类型
        /// </summary>
        public DamageDef stage2DamageDef = DamageDefOf.Blunt;
        
        /// <summary>
        /// 阶段2伤害量
        /// </summary>
        public float stage2DamageAmount = 10f;
        
        /// <summary>
        /// 阶段2击退距离
        /// </summary>
        public int stage2KnockbackDistance = 3;
        
        /// <summary>
        /// 击退是否需要视线
        /// </summary>
        public bool requireLineOfSightForKnockback = true;
        
        /// <summary>
        /// 阶段2效果器
        /// </summary>
        public EffecterDef stage2Effecter;
        
        /// <summary>
        /// 阶段2音效
        /// </summary>
        public SoundDef stage2Sound;
        
        // === 击飞配置 ===
        
        /// <summary>
        /// 击退飞行器定义
        /// </summary>
        public ThingDef knockbackFlyerDef;
        
        /// <summary>
        /// 飞行效果器
        /// </summary>
        public EffecterDef flightEffecterDef;
        
        /// <summary>
        /// 落地音效
        /// </summary>
        public SoundDef landingSound;
        
        // === 调试配置 ===
        
        /// <summary>
        /// 启用调试日志
        /// </summary>
        public bool enableDebugLogging = false;
        
        /// <summary>
        /// 启用调试视觉效果
        /// </summary>
        public bool enableDebugVisuals = false;
        
        /// <summary>
        /// 绘制速度历史图
        /// </summary>
        public bool debugDrawSpeedHistory = false;
        
        public CompProperties_HighSpeedCollision()
        {
            compClass = typeof(CompHighSpeedCollision);
        }
    }
}
