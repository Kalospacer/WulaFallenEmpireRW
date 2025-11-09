using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_Bombardment : CompAbilityEffect_WithDest
    {
        public new CompProperties_AbilityBombardment Props => (CompProperties_AbilityBombardment)props;
        
        // 轰炸状态
        private BombardmentState currentState = BombardmentState.Idle;
        private List<IntVec3> targetCells = new List<IntVec3>();
        private List<BombardmentRow> bombardmentRows = new List<BombardmentRow>();
        private IntVec3 bombardmentCenter;
        private Vector3 bombardmentDirection; // 轰炸前进方向
        private int currentRowIndex = 0;
        private int currentCellIndex = 0;
        private int warmupTicksRemaining = 0;
        private int nextBombardmentTick = 0;
        
        // 视觉效果
        private Effecter areaEffecter;

        // 预览状态
        private List<IntVec3> currentPreviewCells = new List<IntVec3>();

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                Log.Message($"[Bombardment] Starting bombardment at {target.Cell} with direction to {dest.Cell}");
                
                // 计算轰炸区域和方向（基于两个目标点）
                CalculateBombardmentArea(target.Cell, dest.Cell);
                
                // 选择目标格子
                SelectTargetCells();
                
                // 组织成排 - 修复：确保正确的排序
                OrganizeTargetCellsIntoRows();
                
                // 开始前摇
                StartWarmup();
                
                Log.Message($"[Bombardment] Bombardment initialized: {targetCells.Count} targets in {bombardmentRows.Count} rows");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Bombardment] Error starting bombardment: {ex}");
            }
        }

        // 重写：绘制双目标预览
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            if (!Props.showBombardmentArea || parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                // 如果正在选择第二个目标（方向点），则显示轰炸区域预览
                if (selectedTarget.IsValid)
                {
                    // 动态计算轰炸区域（基于第一个目标和当前鼠标位置）
                    CalculateDynamicBombardmentArea(selectedTarget.Cell, target.Cell);
                    
                    // 绘制轰炸区域预览
                    DrawBombardmentAreaPreview(selectedTarget.Cell, target.Cell);
                }
            }
            catch (System.Exception)
            {
                // 忽略预览绘制错误
            }
        }

        // 新增：基于两个目标点动态计算轰炸区域
        private void CalculateDynamicBombardmentArea(IntVec3 startCell, IntVec3 directionCell)
        {
            Map map = parent.pawn.Map;
            
            // 计算轰炸方向（从起点指向方向点）
            Vector3 direction = (directionCell.ToVector3() - startCell.ToVector3()).normalized;
            
            // 如果方向为零向量，使用默认方向
            if (direction == Vector3.zero)
            {
                direction = Vector3.forward;
            }
            
            // 计算轰炸区域的所有单元格
            currentPreviewCells = CalculateBombardmentAreaCells(startCell, direction);
        }

        // 绘制轰炸区域预览（基于两个目标点）
        private void DrawBombardmentAreaPreview(IntVec3 startCell, IntVec3 directionCell)
        {
            Map map = parent.pawn.Map;

            // 绘制轰炸区域内部的单元格
            foreach (var cell in currentPreviewCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.areaPreviewColor, 0.4f);
                }
            }

            // 绘制轰炸区域边界
            DrawBombardmentBoundaries(startCell, directionCell);
        }

        // 绘制轰炸区域边界（基于两个目标点）
        private void DrawBombardmentBoundaries(IntVec3 startCell, IntVec3 directionCell)
        {
            Map map = parent.pawn.Map;
            
            // 计算轰炸方向
            Vector3 direction = (directionCell.ToVector3() - startCell.ToVector3()).normalized;
            
            // 如果方向为零向量，使用默认方向
            if (direction == Vector3.zero)
            {
                direction = Vector3.forward;
            }
            
            // 计算垂直于轰炸方向的方向（作为轰炸区域的宽度方向）
            Vector3 perpendicularDirection = new Vector3(-direction.z, 0, direction.x).normalized;
            
            // 计算轰炸区域的四个角
            Vector3 startCenter = startCell.ToVector3();
            
            float halfWidth = Props.bombardmentWidth * 0.5f;
            float totalLength = Props.bombardmentLength;
            
            // 轰炸起点
            Vector3 startLeft = startCenter + perpendicularDirection * halfWidth;
            Vector3 startRight = startCenter - perpendicularDirection * halfWidth;
            
            // 轰炸终点（沿方向前进轰炸长度）
            Vector3 endCenter = startCenter + direction * totalLength;
            Vector3 endLeft = endCenter + perpendicularDirection * halfWidth;
            Vector3 endRight = endCenter - perpendicularDirection * halfWidth;
            
            // 转换为 IntVec3 并确保在地图范围内
            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);
            
            // 绘制边界线
            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            // 绘制方向箭头
            DrawDirectionArrow(startCell, directionCell);
        }

        // 绘制方向箭头
        private void DrawDirectionArrow(IntVec3 startCell, IntVec3 directionCell)
        {
            Map map = parent.pawn.Map;
            
            Vector3 startPos = startCell.ToVector3Shifted();
            Vector3 directionPos = directionCell.ToVector3Shifted();
            
            // 绘制从起点到方向点的连线
            GenDraw.DrawLineBetween(startPos, directionPos, SimpleColor.Yellow, 0.1f);
            
            // 在方向点绘制箭头标记
            GenDraw.DrawTargetHighlight(directionCell);
        }

        // 计算轰炸区域的所有单元格（基于起点和方向）
        private List<IntVec3> CalculateBombardmentAreaCells(IntVec3 startCell, Vector3 direction)
        {
            var areaCells = new List<IntVec3>();
            Map map = parent.pawn.Map;
            
            Vector3 start = startCell.ToVector3();
            
            // 计算垂直于轰炸方向的方向（宽度方向）
            Vector3 perpendicularDirection = new Vector3(-direction.z, 0, direction.x).normalized;
            
            float halfWidth = Props.bombardmentWidth * 0.5f;
            float totalLength = Props.bombardmentLength;
            
            // 使用浮点步进计算所有单元格
            int widthSteps = Mathf.Max(1, Props.bombardmentWidth);
            int lengthSteps = Mathf.Max(1, Props.bombardmentLength);
            
            for (int l = 0; l <= lengthSteps; l++)
            {
                float lengthProgress = (float)l / lengthSteps;
                float lengthOffset = Mathf.Lerp(0, totalLength, lengthProgress);
                
                for (int w = 0; w <= widthSteps; w++)
                {
                    float widthProgress = (float)w / widthSteps;
                    float widthOffset = Mathf.Lerp(-halfWidth, halfWidth, widthProgress);
                    
                    // 计算单元格位置
                    Vector3 cellPos = start + direction * lengthOffset + perpendicularDirection * widthOffset;
                    
                    IntVec3 cell = new IntVec3(
                        Mathf.RoundToInt(cellPos.x),
                        Mathf.RoundToInt(cellPos.y),
                        Mathf.RoundToInt(cellPos.z)
                    );
                    
                    if (cell.InBounds(map) && !areaCells.Contains(cell))
                    {
                        areaCells.Add(cell);
                    }
                }
            }
            
            return areaCells;
        }

        // 计算轰炸区域和方向（基于两个目标点）
        private void CalculateBombardmentArea(IntVec3 startCell, IntVec3 directionCell)
        {
            bombardmentCenter = startCell;
            
            // 计算轰炸方向（从起点指向方向点）
            Vector3 direction = (directionCell.ToVector3() - startCell.ToVector3()).normalized;
            
            // 如果方向为零向量，使用默认方向
            if (direction == Vector3.zero)
            {
                direction = Vector3.forward;
            }
            
            bombardmentDirection = direction;
            
            Log.Message($"[Bombardment] Bombardment direction: {bombardmentDirection} (from {startCell} to {directionCell})");
        }

        private void SelectTargetCells()
        {
            // 计算轰炸区域的所有单元格
            var areaCells = CalculateBombardmentAreaCells(bombardmentCenter, bombardmentDirection);
            
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
            if (selectedCells.Count < Props.minTargetCells)
            {
                // 补充不足的格子
                int needed = Props.minTargetCells - selectedCells.Count;
                if (missedCells.Count > 0)
                {
                    selectedCells.AddRange(missedCells.InRandomOrder().Take(Mathf.Min(needed, missedCells.Count)));
                }
            }
            else if (selectedCells.Count > Props.maxTargetCells)
            {
                // 随机移除多余的格子
                selectedCells = selectedCells.InRandomOrder().Take(Props.maxTargetCells).ToList();
            }
            
            targetCells = selectedCells;
            Log.Message($"[Bombardment] Selected {targetCells.Count} target cells from {areaCells.Count} area cells");
        }

        // 修复：重新组织目标格子成排，确保正确的渐进顺序
        private void OrganizeTargetCellsIntoRows()
        {
            bombardmentRows.Clear();
            
            // 计算垂直于轰炸方向的方向（宽度方向）
            Vector3 perpendicularDirection = new Vector3(-bombardmentDirection.z, 0, bombardmentDirection.x).normalized;
            
            // 根据轰炸前进方向将格子分组到不同的排
            var rows = new Dictionary<int, List<IntVec3>>();
            
            foreach (var cell in targetCells)
            {
                // 计算格子相对于轰炸起点的"行索引"（在轰炸前进方向上的投影）
                Vector3 cellVector = cell.ToVector3() - bombardmentCenter.ToVector3();
                float dotProduct = Vector3.Dot(cellVector, bombardmentDirection);
                int rowIndex = Mathf.RoundToInt(dotProduct);
                
                if (!rows.ContainsKey(rowIndex))
                {
                    rows[rowIndex] = new List<IntVec3>();
                }
                rows[rowIndex].Add(cell);
            }
            
            // 修复：按照轰炸方向正确排序行索引
            // 从起点（行索引=0）开始，向轰炸方向前进
            var sortedRowIndices = rows.Keys.OrderBy(x => x).ToList();
            
            foreach (var rowIndex in sortedRowIndices)
            {
                // 修复：在每排内按照宽度方向正确排序
                // 从轰炸区域的一侧到另一侧
                var sortedCells = rows[rowIndex].OrderBy(cell => 
                {
                    Vector3 cellVector = cell.ToVector3() - bombardmentCenter.ToVector3();
                    return Vector3.Dot(cellVector, perpendicularDirection);
                }).ToList();
                
                bombardmentRows.Add(new BombardmentRow
                {
                    rowIndex = rowIndex,
                    cells = sortedCells
                });
            }
            
            Log.Message($"[Bombardment] Organized into {bombardmentRows.Count} rows in progressive order");
        }

        private void StartWarmup()
        {
            currentState = BombardmentState.Warmup;
            warmupTicksRemaining = Props.warmupTicks;
            currentRowIndex = 0;
            currentCellIndex = 0;
            
            // 创建区域效果器
            if (Props.showBombardmentArea)
            {
                CreateAreaEffecter();
            }
            
            Log.Message($"[Bombardment] Warmup started: {warmupTicksRemaining} ticks remaining");
        }

        private void UpdateWarmup()
        {
            warmupTicksRemaining--;
            
            if (warmupTicksRemaining <= 0)
            {
                // 前摇结束，开始轰炸
                currentState = BombardmentState.Bombarding;
                nextBombardmentTick = Find.TickManager.TicksGame;
                Log.Message($"[Bombardment] Warmup completed, starting progressive bombardment");
            }
        }

        private void UpdateBombardment()
        {
            if (Find.TickManager.TicksGame < nextBombardmentTick)
                return;
            
            if (currentRowIndex >= bombardmentRows.Count)
            {
                // 所有排都轰炸完毕
                currentState = BombardmentState.Completed;
                Log.Message($"[Bombardment] Progressive bombardment completed");
                return;
            }
            
            var currentRow = bombardmentRows[currentRowIndex];
            
            if (currentCellIndex >= currentRow.cells.Count)
            {
                // 当前排轰炸完毕，移动到下一排
                currentRowIndex++;
                currentCellIndex = 0;
                nextBombardmentTick = Find.TickManager.TicksGame + Props.rowDelayTicks;
                Log.Message($"[Bombardment] Moving to next row {currentRowIndex + 1}/{bombardmentRows.Count}");
                return;
            }
            
            // 轰炸当前格子
            var targetCell = currentRow.cells[currentCellIndex];
            LaunchBombardment(targetCell);
            
            currentCellIndex++;
            nextBombardmentTick = Find.TickManager.TicksGame + Props.impactDelayTicks;
            
            // 记录轰炸进度
            Log.Message($"[Bombardment] Bombarding cell {currentCellIndex}/{currentRow.cells.Count} in row {currentRowIndex + 1}/{bombardmentRows.Count}");
        }

        private void LaunchBombardment(IntVec3 targetCell)
        {
            try
            {
                if (Props.skyfallerDef != null)
                {
                    // 使用 Skyfaller
                    Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerDef);
                    GenSpawn.Spawn(skyfaller, targetCell, parent.pawn.Map);
                    Log.Message($"[Bombardment] Launched skyfaller at {targetCell}");
                }
                else if (Props.projectileDef != null)
                {
                    // 使用抛射体作为备用
                    LaunchProjectileAt(targetCell);
                }
                else
                {
                    Log.Error($"[Bombardment] No skyfaller or projectile defined for bombardment");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Bombardment] Error launching bombardment at {targetCell}: {ex}");
            }
        }

        private void LaunchProjectileAt(IntVec3 targetCell)
        {
            // 从上方发射抛射体
            IntVec3 spawnCell = new IntVec3(targetCell.x, 0, targetCell.z);
            Vector3 spawnPos = spawnCell.ToVector3() + new Vector3(0, 20f, 0); // 从高空发射
            
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
                Log.Message($"[Bombardment] Launched projectile at {targetCell}");
            }
        }

        private void CreateAreaEffecter()
        {
            // 创建轰炸区域视觉效果
            if (DefDatabase<EffecterDef>.GetNamedSilentFail("BombardmentArea") != null)
            {
                areaEffecter = DefDatabase<EffecterDef>.GetNamed("BombardmentArea").Spawn();
                areaEffecter.offset = bombardmentCenter.ToVector3Shifted();
                areaEffecter.scale = Props.effecterScale;
            }
        }

        private void Cleanup()
        {
            // 清理效果器
            areaEffecter?.Cleanup();
            areaEffecter = null;
            
            // 重置状态
            currentState = BombardmentState.Idle;
            targetCells.Clear();
            bombardmentRows.Clear();
            currentPreviewCells.Clear();
            
            Log.Message($"[Bombardment] Cleanup completed");
        }

        private IntVec3 GetSafeMapPosition(IntVec3 pos, Map map)
        {
            if (map == null) return pos;
            
            pos.x = Mathf.Clamp(pos.x, 0, map.Size.x - 1);
            pos.z = Mathf.Clamp(pos.z, 0, map.Size.z - 1);
            
            return pos;
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (currentState == BombardmentState.Idle)
                return;

            switch (currentState)
            {
                case BombardmentState.Warmup:
                    UpdateWarmup();
                    break;
                    
                case BombardmentState.Bombarding:
                    UpdateBombardment();
                    break;
                    
                case BombardmentState.Completed:
                    Cleanup();
                    break;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentState, "currentState", BombardmentState.Idle);
            Scribe_Collections.Look(ref targetCells, "targetCells", LookMode.Value);
            Scribe_Values.Look(ref currentRowIndex, "currentRowIndex", 0);
            Scribe_Values.Look(ref currentCellIndex, "currentCellIndex", 0);
            Scribe_Values.Look(ref warmupTicksRemaining, "warmupTicksRemaining", 0);
            Scribe_Values.Look(ref nextBombardmentTick, "nextBombardmentTick", 0);
        }

        // 重写：获取目标参数
        public override TargetingParameters targetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = false,
            mapObjectTargetsMustBeAutoAttackable = false
        };

        // 重写：验证目标
        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (!target.IsValid)
            {
                return false;
            }

            // 检查是否可以放置目标
            if (!CanPlaceSelectedTargetAt(target))
            {
                if (showMessages)
                {
                    // 修复：使用 LookTargets 而不是 LocalTargetInfo
                    Messages.Message("CannotBombardInvalidLocation".Translate(), new LookTargets(target.Cell, parent.pawn.Map), MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return true;
        }

        // 修复：使用 new 而不是 override，因为基类方法不是 virtual
        public new void DrawHighlight(LocalTargetInfo target)
        {
            if (selectedTarget.IsValid)
            {
                // 当选择第二个目标时，显示轰炸区域
                CalculateDynamicBombardmentArea(selectedTarget.Cell, target.Cell);
                DrawBombardmentAreaPreview(selectedTarget.Cell, target.Cell);
            }
            else
            {
                // 当选择第一个目标时，显示普通高亮
                GenDraw.DrawTargetHighlight(target);
            }
        }

        // 修复：使用 new 而不是 override，因为基类方法不是 virtual
        public new void OnGUI(LocalTargetInfo target)
        {
            Texture2D icon = ((!target.IsValid) ? TexCommand.CannotShoot : parent.def.uiIcon);
            GenUI.DrawMouseAttachment(icon);
            
            string text = ExtraLabelMouseAttachment(target);
            if (!text.NullOrEmpty())
            {
                Widgets.MouseAttachedLabel(text);
            }
        }

        // 重写：额外标签
        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            if (selectedTarget.IsValid)
            {
                return "SelectBombardmentDirection".Translate();
            }
            else
            {
                return "SelectBombardmentStart".Translate();
            }
        }
    }

    // 轰炸状态枚举
    public enum BombardmentState
    {
        Idle,
        Warmup,
        Bombarding,
        Completed
    }

    // 轰炸排数据结构
    public struct BombardmentRow
    {
        public int rowIndex;
        public List<IntVec3> cells;
    }
}
