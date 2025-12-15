using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompEnergyLanceTurret : ThingComp
    {
        public CompProperties_EnergyLanceTurret Props => (CompProperties_EnergyLanceTurret)props;
        
        // 状态变量
        private Pawn currentTarget;
        private EnergyLance activeLance;
        private int lastTargetUpdateTick;
        private int warmupTicksRemaining;
        private int cooldownTicksRemaining;
        private bool isActive = false;
        
        // 位置追踪
        private IntVec3 lastTargetPosition;
        private int lastPositionUpdateTick;
        
        // 调试计数器
        private int debugTickCounter = 0;
        private const int DEBUG_LOG_INTERVAL = 120; // 每2秒输出一次调试信息
        
        // 光束创建保护
        private int lanceCreationTick = -1;
        private const int LANCE_GRACE_PERIOD = 60; // 光束创建后的保护期（1秒）
        
        // 目标丢失保护
        private int targetLostTick = -1;
        private const int TARGET_LOST_GRACE_PERIOD = 60; // 目标丢失后的保护期（1秒）
        
        // 状态追踪
        private TurretState currentState = TurretState.Idle;
        
        private enum TurretState
        {
            Idle,           // 待机
            WarmingUp,      // 预热中
            Firing,         // 发射中
            CoolingDown     // 冷却中
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                ResetState();
            }
            
            WulaLog.Debug($"[EnergyLanceTurret] 炮塔生成在 {parent.Position}, 检测范围: {Props.detectionRange}");
        }

        // 在 StartEnergyLance 方法中修复光束创建逻辑
        private void StartEnergyLance()
        {
            // 双重检查目标有效性
            if (currentTarget == null || !IsTargetValid(currentTarget))
            {
                WulaLog.Debug($"[EnergyLanceTurret] 尝试启动能量光束但目标无效: {(currentTarget == null ? "目标为null" : "目标无效")}");

                // 尝试重新寻找目标
                var potentialTargets = FindPotentialTargets();
                if (potentialTargets.Count > 0)
                {
                    currentTarget = potentialTargets
                        .OrderBy(t => t.Position.DistanceTo(parent.Position))
                        .First();
                    WulaLog.Debug($"[EnergyLanceTurret] 重新获取目标: {currentTarget.LabelCap}");
                }
                else
                {
                    WulaLog.Debug("[EnergyLanceTurret] 无法重新获取目标，进入冷却");
                    StartCooldown();
                    return;
                }
            }

            try
            {
                // 创建能量光束
                var lanceDef = Props.energyLanceDef ?? ThingDef.Named("EnergyLance");
                if (lanceDef == null)
                {
                    WulaLog.Debug("[EnergyLanceTurret] 能量光束定义为空!");
                    StartCooldown();
                    return;
                }

                WulaLog.Debug($"[EnergyLanceTurret] 创建能量光束: {lanceDef.defName} 目标: {currentTarget.LabelCap} 在 {currentTarget.Position}");

                // 关键修复：光束直接在目标位置生成，而不是建筑位置
                activeLance = EnergyLance.MakeEnergyLance(
                    lanceDef,
                    currentTarget.Position,              // 起始位置设置为目标位置
                    currentTarget.Position,              // 目标位置也设置为目标位置
                    parent.Map,
                    Props.energyLanceMoveDistance,
                    false,                               // 不使用固定距离
                    Props.energyLanceDuration,
                    instigatorPawn: null                 // 建筑作为发起者
                );

                if (activeLance == null)
                {
                    WulaLog.Debug("[EnergyLanceTurret] 能量光束创建失败!");
                    StartCooldown();
                    return;
                }

                // 设置光束保护期
                lanceCreationTick = Find.TickManager.TicksGame;

                // 设置建筑引用
                if (activeLance is EnergyLance lance)
                {
                    lance.instigator = parent;
                }

                lastTargetPosition = currentTarget.Position;
                lastPositionUpdateTick = Find.TickManager.TicksGame;
                targetLostTick = -1; // 重置目标丢失计时
                currentState = TurretState.Firing;

                // 立即更新光束位置，确保光束在正确位置开始
                UpdateEnergyLancePosition();

                WulaLog.Debug($"[EnergyLanceTurret] 能量光束启动成功，追踪目标: {currentTarget.LabelCap}");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[EnergyLanceTurret] 启动能量光束错误: {ex}");
                StartCooldown();
            }
        }
        // 改进更新光束位置的方法
        private void UpdateEnergyLancePosition()
        {
            if (activeLance == null || activeLance.Destroyed)
                return;

            // 如果有有效目标，更新光束位置
            if (currentTarget != null && IsTargetValid(currentTarget))
            {
                // 向光束传递目标位置
                UpdateLanceTargetPosition(currentTarget.Position);

                // 添加更多调试信息
                if (debugTickCounter % 30 == 0) // 每0.5秒输出一次位置信息
                {
                    WulaLog.Debug($"[EnergyLanceTurret] 更新光束位置: 目标在 {currentTarget.Position}, 光束在 {activeLance.Position}");
                }
            }
            else if (lastTargetPosition.IsValid && Find.TickManager.TicksGame - lastPositionUpdateTick <= Props.targetUpdateInterval * 2)
            {
                // 使用最后已知位置
                UpdateLanceTargetPosition(lastTargetPosition);
            }
            else
            {
                // 传递空位置
                UpdateLanceTargetPosition(IntVec3.Invalid);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            
            debugTickCounter++;
            
            if (parent.Destroyed || parent.Map == null)
                return;
            
            // 定期输出调试信息
            if (debugTickCounter % DEBUG_LOG_INTERVAL == 0)
            {
                OutputDebugInfo();
            }
            
            // 根据状态处理逻辑
            switch (currentState)
            {
                case TurretState.CoolingDown:
                    HandleCoolingDown();
                    break;
                case TurretState.WarmingUp:
                    HandleWarmingUp();
                    break;
                case TurretState.Firing:
                    HandleFiring();
                    break;
                case TurretState.Idle:
                    HandleIdle();
                    break;
            }
        }
        
        // 处理冷却状态
        private void HandleCoolingDown()
        {
            cooldownTicksRemaining--;
            
            if (debugTickCounter % 30 == 0) // 每0.5秒输出一次冷却信息
            {
                WulaLog.Debug($"[EnergyLanceTurret] 冷却中: {cooldownTicksRemaining} ticks 剩余");
            }
            
            if (cooldownTicksRemaining <= 0)
            {
                WulaLog.Debug("[EnergyLanceTurret] 冷却完成，返回待机状态");
                currentState = TurretState.Idle;
                isActive = false;
            }
        }
        
        // 处理预热状态
        private void HandleWarmingUp()
        {
            // 在预热过程中持续检查目标有效性
            if (currentTarget == null || !IsTargetValid(currentTarget))
            {
                WulaLog.Debug($"[EnergyLanceTurret] 预热过程中目标失效，取消预热");
                ResetState();
                return;
            }
            
            warmupTicksRemaining--;
            
            if (debugTickCounter % 10 == 0) // 每0.17秒输出一次预热信息
            {
                WulaLog.Debug($"[EnergyLanceTurret] 预热中: {warmupTicksRemaining} ticks 剩余, 目标: {currentTarget?.LabelCap ?? "无"}");
            }
            
            if (warmupTicksRemaining <= 0)
            {
                WulaLog.Debug("[EnergyLanceTurret] 预热完成，开始发射光束");
                StartEnergyLance();
            }
        }
        
        // 处理发射状态
        private void HandleFiring()
        {
            // 检查目标状态
            if (Find.TickManager.TicksGame - lastTargetUpdateTick >= Props.targetUpdateInterval)
            {
                UpdateTarget();
                lastTargetUpdateTick = Find.TickManager.TicksGame;
            }
            
            // 更新光束位置
            if (activeLance != null && !activeLance.Destroyed)
            {
                UpdateEnergyLancePosition();
            }
            
            // 检查光束有效性
            CheckEnergyLanceValidity();
        }
        
        // 处理待机状态
        private void HandleIdle()
        {
            // 检查目标状态
            if (Find.TickManager.TicksGame - lastTargetUpdateTick >= Props.targetUpdateInterval)
            {
                UpdateTarget();
                lastTargetUpdateTick = Find.TickManager.TicksGame;
            }
        }
        
        // 输出调试信息
        private void OutputDebugInfo()
        {
            var targets = FindPotentialTargets();
            WulaLog.Debug($"[EnergyLanceTurret] 调试信息:");
            WulaLog.Debug($"  - 状态: {currentState}");
            WulaLog.Debug($"  - 当前目标: {currentTarget?.LabelCap ?? "无"}");
            WulaLog.Debug($"  - 目标位置: {currentTarget?.Position.ToString() ?? "无"}");
            WulaLog.Debug($"  - 活跃光束: {(activeLance != null && !activeLance.Destroyed ? "是" : "否")}");
            WulaLog.Debug($"  - 检测到目标数: {targets.Count}");
            WulaLog.Debug($"  - 冷却剩余: {cooldownTicksRemaining}");
            WulaLog.Debug($"  - 预热剩余: {warmupTicksRemaining}");
            WulaLog.Debug($"  - 是否活跃: {isActive}");
            WulaLog.Debug($"  - 目标丢失保护: {(targetLostTick >= 0 ? (Find.TickManager.TicksGame - targetLostTick) + " ticks前" : "无")}");
            WulaLog.Debug($"  - 光束保护期: {(lanceCreationTick >= 0 ? (Find.TickManager.TicksGame - lanceCreationTick) + " ticks前创建" : "无")}");
            
            // 输出前3个检测到的目标
            for (int i = 0; i < Mathf.Min(3, targets.Count); i++)
            {
                var target = targets[i];
                WulaLog.Debug($"  - 目标{i+1}: {target.LabelCap} 在 {target.Position}, 距离: {target.Position.DistanceTo(parent.Position):F1}");
            }
        }
        
        // 重置状态
        private void ResetState()
        {
            currentTarget = null;
            activeLance = null;
            lastTargetUpdateTick = Find.TickManager.TicksGame;
            warmupTicksRemaining = 0;
            cooldownTicksRemaining = 0;
            isActive = false;
            lanceCreationTick = -1;
            targetLostTick = -1;
            currentState = TurretState.Idle;
        }
        
        // 更新目标
        private void UpdateTarget()
        {
            WulaLog.Debug($"[EnergyLanceTurret] 更新目标检查 - 状态: {currentState}, 活跃光束: {(activeLance != null && !activeLance.Destroyed ? "是" : "否")}");
            
            // 如果没有光束，寻找新目标
            if (activeLance == null || activeLance.Destroyed)
            {
                FindNewTarget();
                return;
            }
            
            // 检查当前目标是否有效
            if (currentTarget != null && IsTargetValid(currentTarget))
            {
                // 更新目标位置
                lastTargetPosition = currentTarget.Position;
                lastPositionUpdateTick = Find.TickManager.TicksGame;
                targetLostTick = -1; // 重置目标丢失计时
                WulaLog.Debug($"[EnergyLanceTurret] 目标仍然有效: {currentTarget.LabelCap}");
                return;
            }
            
            // 当前目标无效，寻找新目标
            FindNewTargetForExistingLance();
        }
        
        // 寻找新目标（首次）
        private void FindNewTarget()
        {
            WulaLog.Debug("[EnergyLanceTurret] 寻找新目标...");
            
            if (currentState != TurretState.Idle)
            {
                WulaLog.Debug($"[EnergyLanceTurret] 无法寻找目标 - 当前状态: {currentState}");
                return;
            }
                
            var potentialTargets = FindPotentialTargets();
            
            if (potentialTargets.Count > 0)
            {
                // 选择最近的敌人
                currentTarget = potentialTargets
                    .OrderBy(t => t.Position.DistanceTo(parent.Position))
                    .First();
                
                WulaLog.Debug($"[EnergyLanceTurret] 发现新目标: {currentTarget.LabelCap} 在 {currentTarget.Position}");
                
                // 开始预热
                StartWarmup();
            }
            else
            {
                WulaLog.Debug("[EnergyLanceTurret] 没有发现有效目标");
            }
        }
        
        // 为现有光束寻找新目标
        private void FindNewTargetForExistingLance()
        {
            if (activeLance == null || activeLance.Destroyed)
                return;
                
            WulaLog.Debug("[EnergyLanceTurret] 为现有光束寻找新目标...");
                
            var potentialTargets = FindPotentialTargets();
            
            if (potentialTargets.Count > 0)
            {
                // 选择离光束最近的敌人
                currentTarget = potentialTargets
                    .OrderBy(t => t.Position.DistanceTo(activeLance.Position))
                    .First();
                
                lastTargetPosition = currentTarget.Position;
                lastPositionUpdateTick = Find.TickManager.TicksGame;
                targetLostTick = -1; // 重置目标丢失计时
                
                WulaLog.Debug($"[EnergyLanceTurret] 切换到新目标: {currentTarget.LabelCap} 在 {currentTarget.Position}");
            }
            else
            {
                // 没有目标，记录目标丢失时间
                if (targetLostTick < 0)
                {
                    targetLostTick = Find.TickManager.TicksGame;
                    WulaLog.Debug($"[EnergyLanceTurret] 目标丢失，开始保护期: {TARGET_LOST_GRACE_PERIOD} ticks");
                }
                
                currentTarget = null;
                lastTargetPosition = IntVec3.Invalid;
                WulaLog.Debug("[EnergyLanceTurret] 没有有效目标，发送空位置");
            }
        }
        
        // 寻找潜在目标
        private List<Pawn> FindPotentialTargets()
        {
            var targets = new List<Pawn>();
            var map = parent.Map;
            
            if (map == null)
                return targets;
            
            // 获取所有在范围内的pawn
            var allPawnsInRange = map.mapPawns.AllPawnsSpawned
                .Where(p => p.Position.DistanceTo(parent.Position) <= Props.detectionRange)
                .ToList();
            
            foreach (var pawn in allPawnsInRange)
            {
                if (IsValidTarget(pawn) && CanShootAtTarget(pawn))
                {
                    targets.Add(pawn);
                }
            }
            
            return targets;
        }
        
        // 检查目标是否有效
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                return false;
                
            if (pawn.Downed || pawn.Dead)
                return false;
                
            // 检查派系关系
            if (pawn.Faction != null)
            {
                bool isHostile = pawn.HostileTo(parent.Faction);
                bool isNeutral = !pawn.HostileTo(parent.Faction) && pawn.Faction != parent.Faction;
                
                if (Props.targetHostileFactions && isHostile)
                    return true;
                else if (Props.targetNeutrals && isNeutral)
                    return true;
                else
                    return false;
            }
            else
            {
                // 无派系的pawn，检查类型
                if (pawn.RaceProps.Animal && !Props.targetAnimals)
                    return false;
                    
                if (pawn.RaceProps.IsMechanoid && !Props.targetMechs)
                    return false;
                
                return true;
            }
        }
        
        // 检查是否可以射击目标
        private bool CanShootAtTarget(Pawn target)
        {
            if (target == null)
                return false;
                
            // 检查视线
            if (Props.requireLineOfSight)
            {
                return GenSight.LineOfSight(parent.Position, target.Position, parent.Map, skipFirstCell: true);
            }
                
            return true;
        }
        
        // 检查目标是否仍然有效
        private bool IsTargetValid(Pawn target)
        {
            return IsValidTarget(target) && 
                   target.Position.DistanceTo(parent.Position) <= Props.detectionRange &&
                   (!Props.requireLineOfSight || GenSight.LineOfSight(parent.Position, target.Position, parent.Map, skipFirstCell: true));
        }
        
        // 开始预热
        private void StartWarmup()
        {
            if (currentTarget == null)
            {
                WulaLog.Debug("[EnergyLanceTurret] 尝试开始预热但没有目标");
                return;
            }
            
            warmupTicksRemaining = Props.warmupTicks;
            isActive = true;
            currentState = TurretState.WarmingUp;
            
            WulaLog.Debug($"[EnergyLanceTurret] 开始预热: {warmupTicksRemaining} ticks, 目标: {currentTarget.LabelCap}");
        }
        
        // 更新光束目标位置
        private void UpdateLanceTargetPosition(IntVec3 targetPos)
        {
            if (activeLance == null || activeLance.Destroyed)
                return;
                
            // 尝试直接转换为EnergyLance并调用方法
            if (activeLance is EnergyLance energyLance)
            {
                energyLance.UpdateTargetPosition(targetPos);
                WulaLog.Debug($"[EnergyLanceTurret] 更新光束目标: {targetPos}");
            }
            else
            {
                // 使用反射调用更新方法
                var moveMethod = activeLance.GetType().GetMethod("UpdateTargetPosition");
                if (moveMethod != null)
                {
                    moveMethod.Invoke(activeLance, new object[] { targetPos });
                    WulaLog.Debug($"[EnergyLanceTurret] 通过反射更新光束目标: {targetPos}");
                }
                else
                {
                    WulaLog.Debug("[EnergyLanceTurret] 无法更新光束目标位置");
                }
            }
        }
        
        // 检查光束有效性
        private void CheckEnergyLanceValidity()
        {
            if (activeLance == null || activeLance.Destroyed)
            {
                // 光束已销毁，进入冷却
                WulaLog.Debug("[EnergyLanceTurret] 光束已销毁，开始冷却");
                StartCooldown();
                return;
            }
            
            // 检查光束是否在保护期内
            if (lanceCreationTick >= 0 && Find.TickManager.TicksGame - lanceCreationTick < LANCE_GRACE_PERIOD)
            {
                // 光束还在保护期内，不检查销毁条件
                return;
            }
            
            // 检查目标丢失保护期
            if (targetLostTick >= 0 && Find.TickManager.TicksGame - targetLostTick > TARGET_LOST_GRACE_PERIOD)
            {
                WulaLog.Debug("[EnergyLanceTurret] 目标丢失保护期结束，销毁光束");
                activeLance.Destroy();
                StartCooldown();
                return;
            }
            
            // 检查光束是否长时间没有收到位置更新
            if (Find.TickManager.TicksGame - lastPositionUpdateTick > Props.targetUpdateInterval * 3)
            {
                WulaLog.Debug("[EnergyLanceTurret] 光束长时间未收到位置更新，销毁");
                activeLance.Destroy();
                StartCooldown();
            }
        }
        
        // 开始冷却
        private void StartCooldown()
        {
            cooldownTicksRemaining = Props.cooldownTicks;
            isActive = false;
            currentTarget = null;
            activeLance = null;
            lanceCreationTick = -1;
            targetLostTick = -1;
            currentState = TurretState.CoolingDown;
            
            WulaLog.Debug($"[EnergyLanceTurret] 开始冷却: {cooldownTicksRemaining} ticks");
        }
        
        // 绘制检测范围
        public override void PostDraw()
        {
            base.PostDraw();
            
            if (Find.Selector.IsSelected(parent))
            {
                // 绘制检测范围
                GenDraw.DrawRadiusRing(parent.Position, Props.detectionRange, Color.red);
                
                // 绘制当前目标
                if (currentTarget != null && !currentTarget.Destroyed)
                {
                    GenDraw.DrawLineBetween(parent.DrawPos, currentTarget.DrawPos, SimpleColor.Red, 0.2f);
                    GenDraw.DrawTargetHighlight(currentTarget.Position);
                }
                
                // 绘制光束状态
                if (activeLance != null && !activeLance.Destroyed)
                {
                    GenDraw.DrawLineBetween(parent.DrawPos, activeLance.DrawPos, SimpleColor.Yellow, 0.3f);
                }
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_References.Look(ref currentTarget, "currentTarget");
            Scribe_References.Look(ref activeLance, "activeLance");
            Scribe_Values.Look(ref lastTargetUpdateTick, "lastTargetUpdateTick", 0);
            Scribe_Values.Look(ref warmupTicksRemaining, "warmupTicksRemaining", 0);
            Scribe_Values.Look(ref cooldownTicksRemaining, "cooldownTicksRemaining", 0);
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref lastTargetPosition, "lastTargetPosition");
            Scribe_Values.Look(ref lastPositionUpdateTick, "lastPositionUpdateTick", 0);
            Scribe_Values.Look(ref lanceCreationTick, "lanceCreationTick", -1);
            Scribe_Values.Look(ref targetLostTick, "targetLostTick", -1);
            Scribe_Values.Look(ref currentState, "currentState", TurretState.Idle);
        }
        
        // 调试信息
        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            string status = currentState.ToString();
            
            string targetInfo = currentTarget != null ? $"\n目标: {currentTarget.LabelCap}" : "";
            string rangeInfo = $"\n检测范围: {Props.detectionRange}";
            
            return string.IsNullOrEmpty(baseString) ? 
                $"{status}{targetInfo}{rangeInfo}" : 
                $"{baseString}\n{status}{targetInfo}{rangeInfo}";
        }
    }
}
