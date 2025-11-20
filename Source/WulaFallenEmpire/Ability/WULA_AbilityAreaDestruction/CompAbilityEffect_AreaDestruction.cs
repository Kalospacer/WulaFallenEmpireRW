using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_AreaDestruction : CompAbilityEffect
    {
        private readonly List<IntVec3> tmpCells = new List<IntVec3>();
        private readonly List<Thing> tmpThings = new List<Thing>();

        private new CompProperties_AbilityAreaDestruction Props => (CompProperties_AbilityAreaDestruction)props;

        private Pawn Pawn => parent.pawn;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            Map map = parent.pawn.MapHeld;
            if (map == null) return;

            // 播放发射特效（在施法者位置）
            PlayCastEffecter(map);

            // 获取扇形区域内的所有单元格
            List<IntVec3> affectedCells = AffectedCells(target);
            
            // 记录所有被影响的目标
            List<Thing> affectedTargets = new List<Thing>();
            
            foreach (IntVec3 cell in affectedCells)
            {
                if (!cell.InBounds(map)) continue;
                
                // 处理该单元格中的所有事物
                tmpThings.Clear();
                tmpThings.AddRange(cell.GetThingList(map));
                
                foreach (Thing thing in tmpThings)
                {
                    if (thing == null || thing.Destroyed) continue;
                    
                    // 检查是否应该影响这个目标
                    if (!ShouldAffectThing(thing)) continue;
                    
                    // 添加到受影响目标列表
                    if (!affectedTargets.Contains(thing))
                    {
                        affectedTargets.Add(thing);
                    }
                }
            }
            
            // 为每个受影响的目标播放命中效果器并处理效果
            foreach (Thing affectedThing in affectedTargets)
            {
                PlayHitEffecter(affectedThing, map);
                ProcessTarget(affectedThing);
            }
        }

        private void PlayCastEffecter(Map map)
        {
            try
            {
                if (Props.castEffecter == null) return;
                
                // 使用与 CompAbilityEffect_EffecterOnCaster 相同的方法
                Effecter effecter = Props.castEffecter.Spawn(Pawn, map);
                
                if (Props.castEffecterMaintainTicks > 0)
                {
                    // 使用与参考代码相同的方法来维持效果器
                    map.effecterMaintainer.AddEffecterToMaintain(effecter, new TargetInfo(Pawn), Pawn, Props.castEffecterMaintainTicks);
                }
                else
                {
                    effecter.Cleanup();
                }
                
                Log.Message($"[AreaDestruction] Played cast effecter on caster at {Pawn.Position}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[AreaDestruction] Error playing cast effecter: {ex.Message}");
            }
        }

        private void PlayHitEffecter(Thing target, Map map)
        {
            try
            {
                if (Props.hitEffecter == null) return;
                if (target == null || target.Destroyed) return;
                
                // 使用与 CompAbilityEffect_EffecterOnTarget 相同的方法
                Effecter effecter;
                if (target is Pawn pawnTarget)
                {
                    effecter = Props.hitEffecter.Spawn(pawnTarget, map);
                }
                else
                {
                    effecter = Props.hitEffecter.Spawn(target.Position, map);
                }
                
                if (Props.hitEffecterMaintainTicks > 0)
                {
                    // 使用与参考代码相同的方法来维持效果器
                    parent.AddEffecterToMaintain(effecter, target.Position, Props.hitEffecterMaintainTicks);
                }
                else
                {
                    effecter.Cleanup();
                }
                
                Log.Message($"[AreaDestruction] Played hit effecter on {target.Label} at {target.Position}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[AreaDestruction] Error playing hit effecter on {target?.Label}: {ex.Message}");
            }
        }

        private void ProcessTarget(Thing target)
        {
            if (target is Building building)
            {
                DestroyBuilding(building);
            }
            else if (target is Pawn targetPawn)
            {
                DestroyAllBodyParts(targetPawn);
            }
        }

        private bool ShouldAffectThing(Thing thing)
        {
            // 检查是否影响施法者自己
            if (thing == Pawn && !Props.affectCaster)
                return false;
                
            // 检查是否影响友方单位
            if (thing is Pawn targetPawn && targetPawn.Faction != null)
            {
                if (!Props.affectAllies && targetPawn.Faction == Pawn.Faction)
                    return false;
                    
                // 不攻击囚犯（除非设置影响友方）
                if (targetPawn.IsPrisoner && targetPawn.HostFaction == Pawn.Faction && !Props.affectAllies)
                    return false;
            }
            
            return true;
        }

        private void DestroyBuilding(Building building)
        {
            try
            {
                if (building.Destroyed || !building.Spawned) return;
                
                // 记录建筑信息用于日志
                string buildingInfo = $"{building.Label} at {building.Position}";
                
                // 直接销毁建筑
                building.Destroy(DestroyMode.Vanish);
                
                Log.Message($"[AreaDestruction] Destroyed building: {buildingInfo}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[AreaDestruction] Error destroying building {building?.Label}: {ex.Message}");
            }
        }

        private void DestroyAllBodyParts(Pawn targetPawn)
        {
            try
            {
                if (targetPawn.Destroyed || !targetPawn.Spawned || targetPawn.Dead) return;
                
                // 记录pawn信息
                string pawnInfo = $"{targetPawn.Label} at {targetPawn.Position}";
                
                // 获取所有身体部位（不包括核心部位如躯干、头部）
                var bodyPartRecords = targetPawn.def.race.body.AllParts;
                
                int partsDestroyed = 0;
                foreach (var bodyPartRecord in bodyPartRecords)
                {
                    // 跳过核心部位以避免立即死亡（可选，根据需求调整）
                    if (IsCoreBodyPart(bodyPartRecord)) continue;
                    
                    // 检查该部位是否已经缺失
                    if (!targetPawn.health.hediffSet.PartIsMissing(bodyPartRecord))
                    {
                        // 添加缺失部位hediff
                        targetPawn.health.AddHediff(HediffDefOf.MissingBodyPart, bodyPartRecord);
                        partsDestroyed++;
                    }
                }
                
                // 如果摧毁了任何部位，检查是否应该杀死pawn
                if (partsDestroyed > 0)
                {
                    // 检查pawn是否还"活着"（没有核心部位缺失时可能还能存活）
                    CheckPawnViability(targetPawn);
                    
                    Log.Message($"[AreaDestruction] Destroyed {partsDestroyed} body parts on {pawnInfo}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[AreaDestruction] Error destroying body parts on {targetPawn?.Label}: {ex.Message}");
            }
        }

        private bool IsCoreBodyPart(BodyPartRecord bodyPart)
        {
            // 定义核心部位，这些部位缺失会导致立即死亡
            return bodyPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource) ||  // 大脑
                   bodyPart.def.tags.Contains(BodyPartTagDefOf.BloodPumpingSource) ||   // 心脏
                   bodyPart.def == BodyPartDefOf.Torso;                                 // 躯干
        }

        private void CheckPawnViability(Pawn pawn)
        {
            // 检查pawn是否还能存活
            if (pawn.Dead) return;
            
            // 如果失去了所有肢体，pawn可能会倒下但不会立即死亡
            bool hasAnyLimbs = false;
            var allParts = pawn.def.race.body.AllParts;
            
            foreach (var part in allParts)
            {
                if ((part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore) || 
                     part.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore)) &&
                    !pawn.health.hediffSet.PartIsMissing(part))
                {
                    hasAnyLimbs = true;
                    break;
                }
            }
            
            // 如果没有肢体了，让pawn倒下
            if (!hasAnyLimbs && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                pawn.health.forceDowned = true;
                pawn.health.CheckForStateChange(null, null);
            }
        }

        public override IEnumerable<PreCastAction> GetPreCastActions()
        {
            // 这里不再预先创建效果器，改为在Apply中创建
            yield break;
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            GenDraw.DrawFieldEdges(AffectedCells(target), Color.red);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            if (Pawn.Faction != null && !Props.affectAllies)
            {
                foreach (IntVec3 cell in AffectedCells(target))
                {
                    List<Thing> thingList = cell.GetThingList(Pawn.Map);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        if (thingList[i].Faction == Pawn.Faction)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private List<IntVec3> AffectedCells(LocalTargetInfo target)
        {
            tmpCells.Clear();
            Vector3 casterPos = Pawn.Position.ToVector3Shifted().Yto0();
            IntVec3 targetCell = target.Cell.ClampInsideMap(Pawn.Map);
            
            if (Pawn.Position == targetCell)
            {
                return tmpCells;
            }

            float distance = (targetCell - Pawn.Position).LengthHorizontal;
            float xRatio = (float)(targetCell.x - Pawn.Position.x) / distance;
            float zRatio = (float)(targetCell.z - Pawn.Position.z) / distance;
            
            // 计算扇形末端位置
            targetCell.x = Mathf.RoundToInt((float)Pawn.Position.x + xRatio * Props.range);
            targetCell.z = Mathf.RoundToInt((float)Pawn.Position.z + zRatio * Props.range);

            float targetAngle = Vector3.SignedAngle(targetCell.ToVector3Shifted().Yto0() - casterPos, Vector3.right, Vector3.up);
            float halfWidth = Props.lineWidthEnd / 2f;
            float coneLength = Mathf.Sqrt(Mathf.Pow((targetCell - Pawn.Position).LengthHorizontal, 2f) + Mathf.Pow(halfWidth, 2f));
            float coneAngle = 57.29578f * Mathf.Asin(halfWidth / coneLength);

            // 遍历范围内的所有单元格
            int radialCellCount = GenRadial.NumCellsInRadius(Props.range);
            for (int i = 0; i < radialCellCount; i++)
            {
                IntVec3 cell = Pawn.Position + GenRadial.RadialPattern[i];
                if (CanUseCell(cell) && 
                    Mathf.Abs(Mathf.DeltaAngle(Vector3.SignedAngle(cell.ToVector3Shifted().Yto0() - casterPos, Vector3.right, Vector3.up), targetAngle)) <= coneAngle)
                {
                    tmpCells.Add(cell);
                }
            }

            // 添加从施法者到目标直线上的单元格
            List<IntVec3> lineCells = GenSight.BresenhamCellsBetween(Pawn.Position, targetCell);
            for (int j = 0; j < lineCells.Count; j++)
            {
                IntVec3 lineCell = lineCells[j];
                if (!tmpCells.Contains(lineCell) && CanUseCell(lineCell))
                {
                    tmpCells.Add(lineCell);
                }
            }

            return tmpCells;

            bool CanUseCell(IntVec3 c)
            {
                if (!c.InBounds(Pawn.Map))
                    return false;
                if (c == Pawn.Position && !Props.affectCaster)
                    return false;
                if (!Props.canHitFilledCells && c.Filled(Pawn.Map))
                    return false;
                if (!c.InHorDistOf(Pawn.Position, Props.range))
                    return false;
                    
                ShootLine resultingLine;
                return parent.verb.TryFindShootLineFromTo(Pawn.Position, c, out resultingLine);
            }
        }
    }
}
