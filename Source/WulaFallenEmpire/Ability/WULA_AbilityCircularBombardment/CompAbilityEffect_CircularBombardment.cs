using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_CircularBombardment : CompAbilityEffect
    {
        public new CompProperties_AbilityCircularBombardment Props => (CompProperties_AbilityCircularBombardment)props;
        
        // 轰炸状态
        private CircularBombardmentState currentState = CircularBombardmentState.Idle;
        private List<IntVec3> targetCells = new List<IntVec3>();
        private List<IntVec3> remainingTargets = new List<IntVec3>();
        private IntVec3 bombardmentCenter;
        private int warmupTicksRemaining = 0;
        private int nextLaunchTick = 0;
        private int launchesCompleted = 0;
        
        // 组内间隔状态
        private List<IntVec3> currentGroupTargets = new List<IntVec3>();
        private int currentGroupIndex = 0;
        private int nextInnerLaunchTick = 0;
        private bool isInGroupLaunch = false;
        
        // 预览状态
        private List<IntVec3> currentPreviewCells = new List<IntVec3>();
        private List<IntVec3> impactPreviewCells = new List<IntVec3>();

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                WulaLog.Debug($"[CircularBombardment] Starting circular bombardment at {target.Cell}");
                
                // 设置轰炸中心
                bombardmentCenter = target.Cell;
                
                // 选择目标格子
                SelectTargetCells();
                
                // 初始化剩余目标列表
                remainingTargets = new List<IntVec3>(targetCells);
                
                // 开始前摇
                StartWarmup();
                
                WulaLog.Debug($"[CircularBombardment] Bombardment initialized: {targetCells.Count} targets, " +
                           $"{Props.simultaneousLaunches} simultaneous launches, " +
                           $"independent intervals: {Props.useIndependentIntervals}");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CircularBombardment] Error starting bombardment: {ex}");
            }
        }

        // 绘制预览效果
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            if (!Props.showBombardmentArea || parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                // 计算预览区域
                CalculatePreviewArea(target.Cell);
                
                // 绘制轰炸区域预览
                DrawCircularAreaPreview(target.Cell);
                
                // 绘制预计落点预览
                if (Props.showImpactPreview)
                {
                    DrawImpactPreview(target.Cell);
                }
            }
            catch (System.Exception)
            {
                // 忽略预览绘制错误
            }
        }

        // 计算预览区域
        private void CalculatePreviewArea(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            currentPreviewCells.Clear();
            impactPreviewCells.Clear();
            
            // 计算圆形区域内的所有单元格
            currentPreviewCells = GenRadial.RadialCellsAround(center, Props.radius, true).ToList();
            
            // 随机选择一些单元格作为预计落点预览
            var potentialTargets = currentPreviewCells
                .Where(cell => cell.InBounds(map))
                .Where(cell => IsValidTargetCell(cell, map))
                .ToList();
                
            // 随机选择预览目标
            int previewCount = Mathf.Min(Props.maxTargets, potentialTargets.Count);
            impactPreviewCells = potentialTargets
                .InRandomOrder()
                .Take(previewCount)
                .ToList();
        }

        // 绘制圆形区域预览
        private void DrawCircularAreaPreview(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            
            // 绘制圆形区域边界
            foreach (var cell in currentPreviewCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.areaPreviewColor, 0.2f);
                }
            }
            
            // 绘制圆形边界线
            DrawCircularBoundary(center);
        }

        // 绘制圆形边界
        private void DrawCircularBoundary(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            
            // 绘制圆形边界（使用多个线段近似）
            int segments = 36; // 36段，每段10度
            float angleStep = 360f / segments;
            
            Vector3 centerPos = center.ToVector3Shifted();
            
            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep * Mathf.Deg2Rad;
                float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;
                
                Vector3 point1 = centerPos + new Vector3(
                    Mathf.Cos(angle1) * Props.radius, 
                    0, 
                    Mathf.Sin(angle1) * Props.radius
                );
                
                Vector3 point2 = centerPos + new Vector3(
                    Mathf.Cos(angle2) * Props.radius, 
                    0, 
                    Mathf.Sin(angle2) * Props.radius
                );
                
                GenDraw.DrawLineBetween(point1, point2, SimpleColor.Orange, 0.2f);
            }
        }

        // 绘制预计落点预览
        private void DrawImpactPreview(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            
            foreach (var cell in impactPreviewCells)
            {
                if (cell.InBounds(map))
                {
                    // 绘制落点标记
                    GenDraw.DrawTargetHighlight(cell);
                    
                    // 绘制落点范围指示
                    GenDraw.DrawRadiusRing(cell, 1f, Color.red, (c) => true);
                }
            }
        }

        // 选择目标格子
        private void SelectTargetCells()
        {
            Map map = parent.pawn.Map;
            
            // 获取圆形区域内的所有单元格
            var areaCells = GenRadial.RadialCellsAround(bombardmentCenter, Props.radius, true)
                .Where(cell => cell.InBounds(map))
                .Where(cell => IsValidTargetCell(cell, map))
                .ToList();
            
            var selectedCells = new List<IntVec3>();
            var missedCells = new List<IntVec3>();
            
            // 根据概率选择目标格子
            foreach (var cell in areaCells)
            {
                if (Rand.Value <= Props.targetSelectionChance)
                {
                    selectedCells.Add(cell);
                }
                else
                {
                    missedCells.Add(cell);
                }
            }
            
            // 应用最小/最大限制
            if (selectedCells.Count < Props.minTargets)
            {
                // 补充不足的格子
                int needed = Props.minTargets - selectedCells.Count;
                if (missedCells.Count > 0)
                {
                    selectedCells.AddRange(missedCells.InRandomOrder().Take(Mathf.Min(needed, missedCells.Count)));
                }
            }
            else if (selectedCells.Count > Props.maxTargets)
            {
                // 随机移除多余的格子
                selectedCells = selectedCells.InRandomOrder().Take(Props.maxTargets).ToList();
            }
            
            targetCells = selectedCells;
            WulaLog.Debug($"[CircularBombardment] Selected {targetCells.Count} target cells from {areaCells.Count} area cells");
        }

        // 检查单元格是否有效
        private bool IsValidTargetCell(IntVec3 cell, Map map)
        {
            // 检查最小距离
            if (Props.minDistanceFromCenter > 0)
            {
                float distance = Vector3.Distance(cell.ToVector3(), bombardmentCenter.ToVector3());
                if (distance < Props.minDistanceFromCenter)
                    return false;
            }
            
            // 检查友军误伤
            if (Props.avoidFriendlyFire)
            {
                var pawnsInCell = map.thingGrid.ThingsListAt(cell).OfType<Pawn>();
                foreach (var pawn in pawnsInCell)
                {
                    if (pawn.Faction != null && pawn.Faction == parent.pawn.Faction)
                        return false;
                }
            }
            
            // 检查建筑物
            if (Props.avoidBuildings)
            {
                var buildingsInCell = map.thingGrid.ThingsListAt(cell).OfType<Building>();
                if (buildingsInCell.Any())
                    return false;
            }
            
            return true;
        }

        private void StartWarmup()
        {
            currentState = CircularBombardmentState.Warmup;
            warmupTicksRemaining = Props.warmupTicks;
            launchesCompleted = 0;
            currentGroupIndex = 0;
            
            WulaLog.Debug($"[CircularBombardment] Warmup started: {warmupTicksRemaining} ticks remaining");
        }

        private void UpdateWarmup()
        {
            warmupTicksRemaining--;
            
            if (warmupTicksRemaining <= 0)
            {
                // 前摇结束，开始发射
                currentState = CircularBombardmentState.Launching;
                nextLaunchTick = Find.TickManager.TicksGame;
                WulaLog.Debug($"[CircularBombardment] Warmup completed, starting launches");
            }
        }

        // 开始新的一组发射
        private void StartNewGroup()
        {
            if (remainingTargets.Count == 0 || launchesCompleted >= Props.maxLaunches)
            {
                currentState = CircularBombardmentState.Completed;
                WulaLog.Debug($"[CircularBombardment] All launches completed: {launchesCompleted}/{Props.maxLaunches}");
                return;
            }
            
            // 选择本组的目标
            int groupSize = Mathf.Min(Props.simultaneousLaunches, remainingTargets.Count);
            currentGroupTargets = remainingTargets.Take(groupSize).ToList();
            remainingTargets.RemoveRange(0, groupSize);
            
            if (Props.useIndependentIntervals)
            {
                // 启用组内独立间隔
                isInGroupLaunch = true;
                nextInnerLaunchTick = Find.TickManager.TicksGame;
                currentGroupIndex++;
                
                WulaLog.Debug($"[CircularBombardment] Starting group {currentGroupIndex} with {currentGroupTargets.Count} targets, using independent intervals");
            }
            else
            {
                // 传统模式：同时发射所有目标
                foreach (var target in currentGroupTargets)
                {
                    if (launchesCompleted < Props.maxLaunches)
                    {
                        LaunchSkyfaller(target);
                        launchesCompleted++;
                    }
                }
                
                // 设置下一组发射时间
                nextLaunchTick = Find.TickManager.TicksGame + Props.launchIntervalTicks;
                WulaLog.Debug($"[CircularBombardment] Launched group {currentGroupIndex + 1} simultaneously: {currentGroupTargets.Count} targets");
            }
        }

        // 更新组内独立发射
        private void UpdateIndependentGroupLaunch()
        {
            if (Find.TickManager.TicksGame < nextInnerLaunchTick)
                return;
                
            if (currentGroupTargets.Count == 0)
            {
                // 当前组发射完毕
                isInGroupLaunch = false;
                nextLaunchTick = Find.TickManager.TicksGame + Props.launchIntervalTicks;
                WulaLog.Debug($"[CircularBombardment] Group {currentGroupIndex} completed");
                return;
            }
            
            // 发射当前目标
            var target = currentGroupTargets[0];
            currentGroupTargets.RemoveAt(0);
            
            LaunchSkyfaller(target);
            launchesCompleted++;
            
            // 设置下一个组内发射时间
            if (currentGroupTargets.Count > 0 && launchesCompleted < Props.maxLaunches)
            {
                nextInnerLaunchTick = Find.TickManager.TicksGame + Props.innerLaunchIntervalTicks;
            }
            else
            {
                // 当前组发射完毕或达到最大发射数量
                isInGroupLaunch = false;
                nextLaunchTick = Find.TickManager.TicksGame + Props.launchIntervalTicks;
            }
            
            WulaLog.Debug($"[CircularBombardment] Launched target in group {currentGroupIndex} ({launchesCompleted}/{Props.maxLaunches})");
        }

        // 更新发射逻辑
        private void UpdateLaunching()
        {
            if (Props.useIndependentIntervals && isInGroupLaunch)
            {
                UpdateIndependentGroupLaunch();
            }
            else
            {
                if (Find.TickManager.TicksGame >= nextLaunchTick)
                {
                    StartNewGroup();
                }
            }
            
            // 检查是否完成
            if (launchesCompleted >= Props.maxLaunches || 
                (remainingTargets.Count == 0 && !isInGroupLaunch))
            {
                currentState = CircularBombardmentState.Completed;
                WulaLog.Debug($"[CircularBombardment] Bombardment completed: {launchesCompleted} launches");
            }
        }

        private void LaunchSkyfaller(IntVec3 targetCell)
        {
            try
            {
                if (Props.skyfallerDef != null)
                {
                    // 使用 Skyfaller
                    Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerDef);
                    GenSpawn.Spawn(skyfaller, targetCell, parent.pawn.Map);
                }
                else if (Props.projectileDef != null)
                {
                    // 使用抛射体作为备用
                    LaunchProjectileAt(targetCell);
                }
                else
                {
                    WulaLog.Debug($"[CircularBombardment] No skyfaller or projectile defined");
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CircularBombardment] Error launching at {targetCell}: {ex}");
            }
        }

        private void LaunchProjectileAt(IntVec3 targetCell)
        {
            // 从上方发射抛射体
            IntVec3 spawnCell = new IntVec3(targetCell.x, 0, targetCell.z);
            Vector3 spawnPos = spawnCell.ToVector3() + new Vector3(0, 20f, 0);
            
            Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, spawnCell, parent.pawn.Map);
            if (projectile != null)
            {
                projectile.Launch(
                    parent.pawn,
                    spawnPos,
                    new LocalTargetInfo(targetCell),
                    new LocalTargetInfo(targetCell),
                    ProjectileHitFlags.All,
                    false
                );
            }
        }

        private void Cleanup()
        {
            // 重置状态
            currentState = CircularBombardmentState.Idle;
            targetCells.Clear();
            remainingTargets.Clear();
            currentGroupTargets.Clear();
            currentPreviewCells.Clear();
            impactPreviewCells.Clear();
            currentGroupIndex = 0;
            isInGroupLaunch = false;
            launchesCompleted = 0;
            
            WulaLog.Debug($"[CircularBombardment] Cleanup completed");
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (currentState == CircularBombardmentState.Idle)
                return;

            switch (currentState)
            {
                case CircularBombardmentState.Warmup:
                    UpdateWarmup();
                    break;
                    
                case CircularBombardmentState.Launching:
                    UpdateLaunching();
                    break;
                    
                case CircularBombardmentState.Completed:
                    Cleanup();
                    break;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentState, "currentState", CircularBombardmentState.Idle);
            Scribe_Collections.Look(ref targetCells, "targetCells", LookMode.Value);
            Scribe_Collections.Look(ref remainingTargets, "remainingTargets", LookMode.Value);
            Scribe_Collections.Look(ref currentGroupTargets, "currentGroupTargets", LookMode.Value);
            Scribe_Values.Look(ref warmupTicksRemaining, "warmupTicksRemaining", 0);
            Scribe_Values.Look(ref nextLaunchTick, "nextLaunchTick", 0);
            Scribe_Values.Look(ref nextInnerLaunchTick, "nextInnerLaunchTick", 0);
            Scribe_Values.Look(ref launchesCompleted, "launchesCompleted", 0);
            Scribe_Values.Look(ref currentGroupIndex, "currentGroupIndex", 0);
            Scribe_Values.Look(ref isInGroupLaunch, "isInGroupLaunch", false);
        }

        // 技能提示信息
        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            string baseInfo = $"圆形轰炸: 半径{Props.radius}格";
            
            if (Props.useIndependentIntervals)
            {
                baseInfo += $"\n组内间隔: {Props.innerLaunchIntervalTicks}刻";
                baseInfo += $"\n每组数量: {Props.simultaneousLaunches}个";
            }
            else
            {
                baseInfo += $"\n同时发射: {Props.simultaneousLaunches}个";
            }
            
            baseInfo += $"\n最大数量: {Props.maxLaunches}个";
            baseInfo += $"\n组间间隔: {Props.launchIntervalTicks}刻";
            
            return baseInfo;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return base.Valid(target, throwMessages) && 
                   parent.pawn != null && 
                   parent.pawn.Map != null &&
                   target.Cell.IsValid &&
                   target.Cell.InBounds(parent.pawn.Map);
        }
    }

    // 圆形轰炸状态枚举
    public enum CircularBombardmentState
    {
        Idle,
        Warmup,
        Launching,
        Completed
    }
}
