// CompAbilityEffect_OrbitalBombardment.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_OrbitalBombardment : CompAbilityEffect
    {
        public new CompProperties_AbilityOrbitalBombardment Props => (CompProperties_AbilityOrbitalBombardment)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                Log.Message($"OrbitalBombardment skill activated by {parent.pawn.Label} at position {parent.pawn.Position}");
                Log.Message($"Target cell: {target.Cell}, Dest: {dest.Cell}");
                
                // 计算起始和结束位置
                IntVec3 startPos, endPos;
                
                if (Props.approachType == ApproachType.Perpendicular)
                {
                    CalculatePerpendicularPath(target, out startPos, out endPos);
                }
                else
                {
                    startPos = CalculateStartPosition(target);
                    endPos = CalculateEndPosition(target, startPos);
                }
                
                // 确保位置安全
                startPos = GetSafeMapPosition(startPos, parent.pawn.Map);
                endPos = GetSafeMapPosition(endPos, parent.pawn.Map);
                
                Log.Message($"Final positions - Start: {startPos}, End: {endPos}");
                
                // 验证位置是否有效
                if (!startPos.InBounds(parent.pawn.Map))
                {
                    Log.Warning($"Start position {startPos} is out of bounds, adjusting to map center");
                    startPos = parent.pawn.Map.Center;
                }
                
                if (!endPos.InBounds(parent.pawn.Map))
                {
                    Log.Warning($"End position {endPos} is out of bounds, adjusting to map center");
                    endPos = parent.pawn.Map.Center;
                }
                
                // 确保起点和终点不同
                if (startPos == endPos)
                {
                    Log.Warning($"OrbitalBombardment start and end positions are the same: {startPos}. Adjusting end position.");
                    IntVec3 randomOffset = new IntVec3(Rand.Range(-10, 11), 0, Rand.Range(-10, 11));
                    endPos += randomOffset;
                    endPos = GetSafeMapPosition(endPos, parent.pawn.Map);
                }
                
                // 创建轨道炮击飞越
                CreateOrbitalBombardmentFlyOver(startPos, endPos, target.Cell);
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error spawning orbital bombardment: {ex}");
            }
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);

            if (parent.pawn != null && parent.pawn.Map != null)
            {
                Map map = parent.pawn.Map;
                
                try
                {
                    // 计算飞行路径
                    IntVec3 startPos, endPos;
                    if (Props.approachType == ApproachType.Perpendicular)
                    {
                        CalculatePerpendicularPath(target, out startPos, out endPos);
                    }
                    else
                    {
                        startPos = CalculateStartPosition(target);
                        endPos = CalculateEndPosition(target, startPos);
                    }

                    // 确保位置在地图范围内
                    startPos = GetSafeMapPosition(startPos, map);
                    endPos = GetSafeMapPosition(endPos, map);

                    // 检查预览稳定性
                    if (!IsPreviewStable(startPos, endPos, map))
                    {
                        return;
                    }

                    // 绘制炮击区域预览
                    DrawBombardmentAreaPreview(startPos, endPos, target.Cell);
                }
                catch (System.Exception)
                {
                    // 忽略预览绘制中的错误
                }
            }
        }

        // 绘制炮击区域预览
        private void DrawBombardmentAreaPreview(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            Map map = parent.pawn.Map;

            // 计算飞行方向
            Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            if (flightDirection == Vector3.zero)
            {
                flightDirection = Vector3.forward;
            }

            // 计算炮击影响区域的单元格
            List<IntVec3> bombardmentImpactCells = CalculateBombardmentImpactCells(targetCell, flightDirection);

            // 绘制炮击影响区域的预览单元格
            foreach (IntVec3 cell in bombardmentImpactCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.bombardmentPreviewColor, 0.5f);
                }
            }

            // 绘制飞行路径线
            GenDraw.DrawLineBetween(startPos.ToVector3Shifted(), endPos.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);

            // 绘制炮击范围边界
            DrawBombardmentBoundaries(targetCell, flightDirection);
        }

        // 计算炮击影响区域的单元格
        private List<IntVec3> CalculateBombardmentImpactCells(IntVec3 targetCell, Vector3 flightDirection)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = parent.pawn.Map;
            
            // 计算垂直于飞行方向的方向
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            
            // 以目标单元格为中心计算炮击区域
            Vector3 targetCenter = targetCell.ToVector3();
            
            // 计算炮击区域的起始和结束位置（基于炮击长度，以目标为中心）
            float bombardmentHalfLength = Props.bombardmentLength * 0.5f;
            Vector3 bombardmentStart = targetCenter - flightDirection * bombardmentHalfLength;
            Vector3 bombardmentEnd = targetCenter + flightDirection * bombardmentHalfLength;
            
            // 使用整数步进
            int steps = Mathf.Max(1, Mathf.CeilToInt(Props.bombardmentLength));
            for (int i = 0; i <= steps; i++)
            {
                float progress = (float)i / steps;
                Vector3 centerPoint = Vector3.Lerp(bombardmentStart, bombardmentEnd, progress);
                
                // 在垂直方向扩展炮击宽度
                for (int w = -Props.bombardmentWidth; w <= Props.bombardmentWidth; w++)
                {
                    Vector3 offset = perpendicular * w;
                    Vector3 cellPos = centerPoint + offset;
                    
                    // 使用精确的单元格转换
                    IntVec3 cell = new IntVec3(
                        Mathf.RoundToInt(cellPos.x),
                        Mathf.RoundToInt(cellPos.y), 
                        Mathf.RoundToInt(cellPos.z)
                    );
                    
                    if (cell.InBounds(map) && !cells.Contains(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }
            
            Log.Message($"Bombardment Area: Calculated {cells.Count} impact cells centered at {targetCell}");
            return cells;
        }

        // 绘制炮击范围边界
        private void DrawBombardmentBoundaries(IntVec3 targetCell, Vector3 flightDirection)
        {
            Map map = parent.pawn.Map;
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            
            // 以目标单元格为中心
            Vector3 targetCenter = targetCell.ToVector3();
            
            // 计算炮击区域的起始和结束位置
            float bombardmentHalfLength = Props.bombardmentLength * 0.5f;
            Vector3 bombardmentStart = targetCenter - flightDirection * bombardmentHalfLength;
            Vector3 bombardmentEnd = targetCenter + flightDirection * bombardmentHalfLength;
            
            // 计算炮击区域的四个角
            Vector3 startLeft = bombardmentStart + perpendicular * Props.bombardmentWidth;
            Vector3 startRight = bombardmentStart - perpendicular * Props.bombardmentWidth;
            Vector3 endLeft = bombardmentEnd + perpendicular * Props.bombardmentWidth;
            Vector3 endRight = bombardmentEnd - perpendicular * Props.bombardmentWidth;
            
            // 转换为 IntVec3 并确保在地图范围内
            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);
            
            // 绘制边界线
            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);
            
            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);
            
            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);
            
            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);
        }

        // 预处理炮击目标单元格
        private List<IntVec3> PreprocessBombardmentTargets(List<IntVec3> potentialTargets, float fireChance)
        {
            List<IntVec3> confirmedTargets = new List<IntVec3>();
            List<IntVec3> missedCells = new List<IntVec3>();
            
            foreach (IntVec3 cell in potentialTargets)
            {
                if (Rand.Value <= fireChance)
                {
                    confirmedTargets.Add(cell);
                }
                else
                {
                    missedCells.Add(cell);
                }
            }

            // 应用最小和最大炮击数限制
            if (Props.maxBombardmentCount > -1 && confirmedTargets.Count > Props.maxBombardmentCount)
            {
                confirmedTargets = confirmedTargets.InRandomOrder().Take(Props.maxBombardmentCount).ToList();
            }
            
            if (Props.minBombardmentCount > -1 && confirmedTargets.Count < Props.minBombardmentCount)
            {
                int needed = Props.minBombardmentCount - confirmedTargets.Count;
                if (needed > 0 && missedCells.Count > 0)
                {
                    confirmedTargets.AddRange(missedCells.InRandomOrder().Take(Mathf.Min(needed, missedCells.Count)));
                }
            }
            
            Log.Message($"Bombardment Preprocess: {confirmedTargets.Count}/{potentialTargets.Count} cells confirmed after min/max adjustment.");
            return confirmedTargets;
        }

        // 创建轨道炮击飞越
        private void CreateOrbitalBombardmentFlyOver(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            ThingDef flyOverDef = Props.flyOverDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveCorvette");
            if (flyOverDef == null)
            {
                Log.Warning("No fly over def specified for orbital bombardment fly over");
                return;
            }

            FlyOver flyOver = FlyOver.MakeFlyOver(
                flyOverDef,
                startPos,
                endPos,
                parent.pawn.Map,
                Props.flightSpeed,
                Props.altitude,
                casterPawn: parent.pawn
            );

            // 设置基本属性
            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;
            
            // 获取轨道炮击组件并设置预处理后的目标单元格
            CompOrbitalBombardment bombardmentComp = flyOver.GetComp<CompOrbitalBombardment>();
            if (bombardmentComp != null)
            {
                // 计算炮击区域的所有单元格，以目标单元格为中心
                Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                List<IntVec3> potentialTargetCells = CalculateBombardmentImpactCells(targetCell, flightDirection);
                
                if (potentialTargetCells.Count > 0)
                {
                    // 预处理：根据概率筛选实际会被炮击的单元格
                    List<IntVec3> confirmedTargetCells = PreprocessBombardmentTargets(
                        potentialTargetCells,
                        Props.bombardmentFireChance
                    );
                    
                    if (confirmedTargetCells.Count > 0)
                    {
                        bombardmentComp.SetConfirmedTargets(confirmedTargetCells);
                    }
                    else
                    {
                        Log.Warning("No confirmed target cells after preprocessing!");
                    }
                }
                else
                {
                    Log.Error("No potential target cells calculated for orbital bombardment!");
                }
            }
            else
            {
                Log.Error("FlyOver def does not have CompOrbitalBombardment component!");
            }
        }

        // 以下方法与 CompAbilityEffect_SpawnFlyOver 中的相同，需要复制过来
        private IntVec3 GetSafeMapPosition(IntVec3 pos, Map map)
        {
            if (map == null) return pos;
            
            pos.x = Mathf.Clamp(pos.x, 0, map.Size.x - 1);
            pos.z = Mathf.Clamp(pos.z, 0, map.Size.z - 1);
            
            return pos;
        }

        private bool IsPreviewStable(IntVec3 startPos, IntVec3 endPos, Map map)
        {
            if (map == null) return false;
            
            if (!startPos.IsValid || !endPos.IsValid) return false;
            
            if (!startPos.InBounds(map) || !endPos.InBounds(map)) return false;
            
            float distance = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            if (distance < 5f) return false;
            
            return true;
        }

        private void CalculatePerpendicularPath(LocalTargetInfo target, out IntVec3 startPos, out IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 casterPos = parent.pawn.Position;
            IntVec3 targetPos = target.Cell;

            Log.Message($"Calculating perpendicular path: Caster={casterPos}, Target={targetPos}");

            Vector3 directionToTarget = (targetPos.ToVector3() - casterPos.ToVector3()).normalized;
            
            if (directionToTarget == Vector3.zero)
            {
                directionToTarget = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {directionToTarget}");
            }

            Vector3 perpendicularDirection = new Vector3(-directionToTarget.z, 0, directionToTarget.x).normalized;
            
            Log.Message($"Perpendicular direction: {perpendicularDirection}");

            IntVec3 edge1 = FindMapEdgeInDirection(map, targetPos, perpendicularDirection);
            IntVec3 edge2 = FindMapEdgeInDirection(map, targetPos, -perpendicularDirection);

            if (Rand.Value < 0.5f)
            {
                startPos = edge1;
                endPos = edge2;
            }
            else
            {
                startPos = edge2;
                endPos = edge1;
            }

            Log.Message($"Perpendicular path: {startPos} -> {targetPos} -> {endPos}");
        }

        private IntVec3 FindMapEdgeInDirection(Map map, IntVec3 fromPos, Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }
            
            IntVec3 mapCenter = map.Center;
            IntVec3 mapSize = new IntVec3(map.Size.x, 0, map.Size.z);
            
            Vector3 fromVec = fromPos.ToVector3();
            Vector3 dirNormalized = direction.normalized;
            
            float tMin = float.MaxValue;
            IntVec3? bestEdgePos = null;
            
            for (int i = 0; i < 4; i++)
            {
                float t = 0f;
                IntVec3 edgePos = IntVec3.Invalid;
                
                switch (i)
                {
                    case 0: // 左边界 (x = 0)
                        if (Mathf.Abs(dirNormalized.x) > 0.001f)
                        {
                            t = (0 - fromVec.x) / dirNormalized.x;
                            if (t > 0)
                            {
                                float z = fromVec.z + dirNormalized.z * t;
                                if (z >= 0 && z < map.Size.z)
                                {
                                    edgePos = new IntVec3(0, 0, Mathf.RoundToInt(z));
                                }
                            }
                        }
                        break;
                        
                    case 1: // 右边界 (x = map.Size.x - 1)
                        if (Mathf.Abs(dirNormalized.x) > 0.001f)
                        {
                            t = (map.Size.x - 1 - fromVec.x) / dirNormalized.x;
                            if (t > 0)
                            {
                                float z = fromVec.z + dirNormalized.z * t;
                                if (z >= 0 && z < map.Size.z)
                                {
                                    edgePos = new IntVec3(map.Size.x - 1, 0, Mathf.RoundToInt(z));
                                }
                            }
                        }
                        break;
                        
                    case 2: // 下边界 (z = 0)
                        if (Mathf.Abs(dirNormalized.z) > 0.001f)
                        {
                            t = (0 - fromVec.z) / dirNormalized.z;
                            if (t > 0)
                            {
                                float x = fromVec.x + dirNormalized.x * t;
                                if (x >= 0 && x < map.Size.x)
                                {
                                    edgePos = new IntVec3(Mathf.RoundToInt(x), 0, 0);
                                }
                            }
                        }
                        break;
                        
                    case 3: // 上边界 (z = map.Size.z - 1)
                        if (Mathf.Abs(dirNormalized.z) > 0.001f)
                        {
                            t = (map.Size.z - 1 - fromVec.z) / dirNormalized.z;
                            if (t > 0)
                            {
                                float x = fromVec.x + dirNormalized.x * t;
                                if (x >= 0 && x < map.Size.x)
                                {
                                    edgePos = new IntVec3(Mathf.RoundToInt(x), 0, map.Size.z - 1);
                                }
                            }
                        }
                        break;
                }
                
                if (edgePos.IsValid && edgePos.InBounds(map) && t > 0 && t < tMin)
                {
                    tMin = t;
                    bestEdgePos = edgePos;
                }
            }
            
            if (bestEdgePos.HasValue)
            {
                return bestEdgePos.Value;
            }
            
            Log.Warning($"Could not find map edge in direction {direction}, using random edge");
            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 GetRandomMapEdgePosition(Map map)
        {
            int edge = Rand.Range(0, 4);
            int x, z;
            
            switch (edge)
            {
                case 0: // 上边
                    x = Rand.Range(0, map.Size.x);
                    z = 0;
                    break;
                case 1: // 右边
                    x = map.Size.x - 1;
                    z = Rand.Range(0, map.Size.z);
                    break;
                case 2: // 下边
                    x = Rand.Range(0, map.Size.x);
                    z = map.Size.z - 1;
                    break;
                case 3: // 左边
                default:
                    x = 0;
                    z = Rand.Range(0, map.Size.z);
                    break;
            }
            
            IntVec3 edgePos = new IntVec3(x, 0, z);
            Log.Message($"Random map edge position: {edgePos}");
            return edgePos;
        }

        private IntVec3 CalculateStartPosition(LocalTargetInfo target)
        {
            Map map = parent.pawn.Map;
            
            switch (Props.startPosition)
            {
                case StartPosition.Caster:
                    return parent.pawn.Position;
                    
                case StartPosition.MapEdge:
                    return GetMapEdgePosition(map, GetDirectionFromCasterToTarget(target));
                    
                case StartPosition.CustomOffset:
                    return GetSafeMapPosition(parent.pawn.Position + Props.customStartOffset, map);
                    
                case StartPosition.RandomMapEdge:
                    return GetRandomMapEdgePosition(map);
                    
                default:
                    return parent.pawn.Position;
            }
        }

        private IntVec3 CalculateEndPosition(LocalTargetInfo target, IntVec3 startPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 endPos;

            switch (Props.endPosition)
            {
                case EndPosition.TargetCell:
                    endPos = target.Cell;
                    break;

                case EndPosition.OppositeMapEdge:
                    endPos = GetOppositeMapEdgeThroughCenter(map, startPos);
                    break;

                case EndPosition.CustomOffset:
                    endPos = GetSafeMapPosition(target.Cell + Props.customEndOffset, map);
                    break;

                case EndPosition.FixedDistance:
                    endPos = GetFixedDistancePosition(startPos, target.Cell);
                    break;

                case EndPosition.RandomMapEdge:
                    endPos = GetRandomMapEdgePosition(map);
                    Log.Message($"Random map edge selected as end position: {endPos}");
                    break;

                default:
                    endPos = target.Cell;
                    break;
            }

            return GetSafeMapPosition(endPos, map);
        }

        private IntVec3 GetOppositeMapEdgeThroughCenter(Map map, IntVec3 startPos)
        {
            IntVec3 center = map.Center;
            Vector3 toCenter = (center.ToVector3() - startPos.ToVector3()).normalized;
            
            if (toCenter == Vector3.zero)
            {
                toCenter = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction to center: {toCenter}");
            }
            
            Vector3 fromCenter = toCenter;
            IntVec3 oppositeEdge = GetMapEdgePositionFromCenter(map, fromCenter);
            
            Log.Message($"Found opposite edge through center: {oppositeEdge}");
            return oppositeEdge;
        }

        private IntVec3 GetMapEdgePositionFromCenter(Map map, Vector3 direction)
        {
            IntVec3 center = map.Center;
            float maxDist = Mathf.Max(map.Size.x, map.Size.z) * 0.6f;
            
            for (int i = 1; i <= maxDist; i++)
            {
                IntVec3 testPos = center + new IntVec3(
                    Mathf.RoundToInt(direction.x * i),
                    0,
                    Mathf.RoundToInt(direction.z * i));
                    
                if (!testPos.InBounds(map))
                {
                    IntVec3 edgePos = FindClosestValidPosition(testPos, map);
                    Log.Message($"Found map edge from center: {edgePos} (direction: {direction}, distance: {i})");
                    return edgePos;
                }
            }
            
            Log.Warning("Could not find map edge from center, using random edge");
            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 GetMapEdgePosition(Map map, Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {direction}");
            }
            
            IntVec3 center = map.Center;
            float maxDist = Mathf.Max(map.Size.x, map.Size.z) * 0.6f;
            
            for (int i = 1; i <= maxDist; i++)
            {
                IntVec3 testPos = center + new IntVec3(
                    Mathf.RoundToInt(direction.x * i),
                    0,
                    Mathf.RoundToInt(direction.z * i));
                    
                if (!testPos.InBounds(map))
                {
                    IntVec3 edgePos = FindClosestValidPosition(testPos, map);
                    Log.Message($"Found map edge position: {edgePos} (direction: {direction}, distance: {i})");
                    return edgePos;
                }
            }
            
            Log.Warning("Could not find map edge in direction, using random edge");
            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 FindClosestValidPosition(IntVec3 invalidPos, Map map)
        {
            for (int radius = 1; radius <= 5; radius++)
            {
                foreach (IntVec3 pos in GenRadial.RadialPatternInRadius(radius))
                {
                    IntVec3 testPos = invalidPos + pos;
                    if (testPos.InBounds(map))
                    {
                        return testPos;
                    }
                }
            }
            
            return map.Center;
        }

        private IntVec3 GetFixedDistancePosition(IntVec3 startPos, IntVec3 targetPos)
        {
            Vector3 direction = (targetPos.ToVector3() - startPos.ToVector3()).normalized;
            IntVec3 endPos = startPos + new IntVec3(
                (int)(direction.x * Props.flyOverDistance),
                0,
                (int)(direction.z * Props.flyOverDistance));
            
            Log.Message($"Fixed distance position: {endPos} (from {startPos}, distance: {Props.flyOverDistance})");
            return endPos;
        }

        private Vector3 GetDirectionFromCasterToTarget(LocalTargetInfo target)
        {
            Vector3 direction = (target.Cell.ToVector3() - parent.pawn.Position.ToVector3()).normalized;
            
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {direction}");
            }
            
            return direction;
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            return $"炮击区域: {Props.bombardmentWidth * 2 + 1}格宽度 × {Props.bombardmentLength}格长度";
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
    
    public class CompProperties_AbilityOrbitalBombardment : CompProperties_AbilityEffect
    {
        public ThingDef flyOverDef;                    // 飞越物体的 ThingDef
        public ApproachType approachType = ApproachType.Standard; // 进场类型
        public float flightSpeed = 1f;                 // 飞行速度
        public float altitude = 20f;                   // 飞行高度
        public bool dropContentsOnImpact = false;      // 是否在终点投放内容物
        public bool playFlyOverSound = true;           // 是否播放飞越音效

        // 起始位置选项
        public StartPosition startPosition = StartPosition.Caster;
        public IntVec3 customStartOffset = IntVec3.Zero;

        // 终点位置选项  
        public EndPosition endPosition = EndPosition.TargetCell;
        public IntVec3 customEndOffset = IntVec3.Zero;
        public int flyOverDistance = 30;               // 飞越距离

        // 炮击配置
        public int bombardmentWidth = 3;               // 炮击宽度
        public int bombardmentLength = 15;             // 炮击长度
        public float bombardmentFireChance = 0.6f;     // 炮击发射概率
        public int minBombardmentCount = -1;           // 最小炮击数
        public int maxBombardmentCount = -1;           // 最大炮击数

        // 炮击可视化
        public bool showBombardmentPreview = true;     // 是否显示炮击预览
        public Color bombardmentPreviewColor = new Color(1f, 1f, 0.3f, 0.3f); // 黄色预览

        public CompProperties_AbilityOrbitalBombardment()
        {
            this.compClass = typeof(CompAbilityEffect_OrbitalBombardment);
        }
    }
}
