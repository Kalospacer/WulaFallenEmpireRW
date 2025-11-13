using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using Verse.Sound;
using System.Reflection;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_DeleteTarget : CompAbilityEffect
    {
        public new CompProperties_AbilityDeleteTarget Props => (CompProperties_AbilityDeleteTarget)props;
        
        // 使用反射访问私有字段
        private static FieldInfo thingIDField = typeof(Thing).GetField("thingIDNumber", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo mapIndexField = typeof(Thing).GetField("mapIndexOrState", BindingFlags.Instance | BindingFlags.NonPublic);
        
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || !target.IsValid)
                return;

            // 尝试删除任何东西，包括地形以外的所有对象
            TryDeleteEverythingAt(target.Cell);
        }

        private void TryDeleteEverythingAt(IntVec3 cell)
        {
            Map map = parent.pawn.Map;
            if (map == null)
                return;

            bool deletedSomething = false;
            int deletionCount = 0;

            try
            {
                // 获取该位置的所有物体（创建副本，因为我们要修改集合）
                List<Thing> thingsAtCell = new List<Thing>(map.thingGrid.ThingsAt(cell));
                
                foreach (Thing thing in thingsAtCell)
                {
                    if (thing != null && CanAffectTarget(new LocalTargetInfo(thing)))
                    {
                        if (ForceRemoveThing(thing, map))
                        {
                            deletedSomething = true;
                            deletionCount++;
                        }
                    }
                }

                // 显示效果
                if (deletedSomething && Props.showEffect)
                {
                    ShowDeleteEffect(cell);
                }

                // 播放音效
                if (deletedSomething && Props.soundEffect != null)
                {
                    Props.soundEffect.PlayOneShot(new TargetInfo(cell, map));
                }

                Log.Message($"[DeleteTarget] Processed cell {cell}, deleted {deletionCount} objects");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[DeleteTarget] Error deleting objects at {cell}: {ex}");
            }
        }

        private bool CanAffectTarget(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            if (thing == null)
                return false;

            // 调试模式：如果设置了affectEverything，忽略所有过滤
            if (Props.affectEverything)
                return true;

            // 根据类型过滤
            if (thing is Building && !Props.affectBuildings)
                return false;
                
            if (thing is Pawn && !Props.affectPawns)
                return false;
                
            if (thing is Plant && !Props.affectPlants)
                return false;
                
            if (thing.def.EverHaulable && !Props.affectItems)
                return false;

            if (thing is Filth && !Props.affectFilth)
                return false;

            if (thing is Blueprint && !Props.affectBlueprints)
                return false;

            if (thing is Frame && !Props.affectFrames)
                return false;

            if (thing is Corpse && !Props.affectCorpses)
                return false;

            if (thing is Building_Trap && !Props.affectMines)
                return false;

            return true;
        }

        private bool ForceRemoveThing(Thing thing, Map map)
        {
            string thingInfo = $"{thing.Label} ({thing.def.defName}) at {thing.Position}";

            try
            {
                // 方法1: 尝试使用 DeSpawn
                if (thing.Spawned)
                {
                    thing.DeSpawn(DestroyMode.Vanish);
                    Log.Message($"[DeleteTarget] Method1 - DeSpawn: {thingInfo}");
                    return true;
                }
            }
            catch (System.Exception ex1)
            {
                Log.Warning($"[DeleteTarget] Method1 failed for {thingInfo}: {ex1}");
            }

            try
            {
                // 方法2: 直接操作 thingGrid（使用反射）
                if (thing.Spawned)
                {
                    ForceRemoveFromThingGrid(thing, map);
                    Log.Message($"[DeleteTarget] Method2 - ForceRemoveFromThingGrid: {thingInfo}");
                    return true;
                }
            }
            catch (System.Exception ex2)
            {
                Log.Warning($"[DeleteTarget] Method2 failed for {thingInfo}: {ex2}");
            }

            try
            {
                // 方法3: 使用反射设置内部状态
                if (thing.Spawned)
                {
                    ForceDespawnViaReflection(thing, map);
                    Log.Message($"[DeleteTarget] Method3 - ForceDespawnViaReflection: {thingInfo}");
                    return true;
                }
            }
            catch (System.Exception ex3)
            {
                Log.Warning($"[DeleteTarget] Method3 failed for {thingInfo}: {ex3}");
            }

            try
            {
                // 方法4: 最后的尝试 - 直接调用内部清理方法
                if (thing.Spawned)
                {
                    CallInternalCleanup(thing, map);
                    Log.Message($"[DeleteTarget] Method4 - CallInternalCleanup: {thingInfo}");
                    return true;
                }
            }
            catch (System.Exception ex4)
            {
                Log.Warning($"[DeleteTarget] Method4 failed for {thingInfo}: {ex4}");
            }

            Log.Error($"[DeleteTarget] All methods failed for: {thingInfo}");
            return false;
        }

        private void ForceRemoveFromThingGrid(Thing thing, Map map)
        {
            try
            {
                // 使用反射调用 thingGrid 的内部移除方法
                MethodInfo deregisterMethod = typeof(ThingGrid).GetMethod("Deregister", BindingFlags.Instance | BindingFlags.NonPublic);
                if (deregisterMethod != null)
                {
                    deregisterMethod.Invoke(map.thingGrid, new object[] { thing });
                }
                else
                {
                    // 备用方法：手动从所有相关格子中移除
                    ManualRemoveFromGrid(thing, map);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[DeleteTarget] ForceRemoveFromThingGrid failed: {ex}");
                throw;
            }
        }

        private void ManualRemoveFromGrid(Thing thing, Map map)
        {
            // 对于多格物体
            if (thing.def.size != IntVec2.One)
            {
                foreach (IntVec3 cell in thing.OccupiedRect())
                {
                    RemoveThingFromCell(thing, cell, map);
                }
            }
            else
            {
                // 对于单格物体
                RemoveThingFromCell(thing, thing.Position, map);
            }
        }

        private void RemoveThingFromCell(Thing thing, IntVec3 cell, Map map)
        {
            try
            {
                // 使用反射访问 thingGrid 的 grid 字段
                FieldInfo gridField = typeof(ThingGrid).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic);
                if (gridField != null)
                {
                    var grid = gridField.GetValue(map.thingGrid) as List<Thing>[];
                    if (grid != null && cell.InBounds(map))
                    {
                        int index = map.cellIndices.CellToIndex(cell);
                        if (index >= 0 && index < grid.Length)
                        {
                            grid[index]?.Remove(thing);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[DeleteTarget] RemoveThingFromCell failed: {ex}");
            }
        }

        private void ForceDespawnViaReflection(Thing thing, Map map)
        {
            try
            {
                // 设置 mapIndexOrState 为 -1（未生成状态）
                if (mapIndexField != null)
                {
                    mapIndexField.SetValue(thing, -1);
                }

                // 设置 spawned 为 false
                FieldInfo spawnedField = typeof(Thing).GetField("spawned", BindingFlags.Instance | BindingFlags.NonPublic);
                if (spawnedField != null)
                {
                    spawnedField.SetValue(thing, false);
                }

                // 设置 destroyed 为 true
                FieldInfo destroyedField = typeof(Thing).GetField("destroyed", BindingFlags.Instance | BindingFlags.NonPublic);
                if (destroyedField != null)
                {
                    destroyedField.SetValue(thing, true);
                }

                // 从地图列表中移除
                MethodInfo notifyMethod = typeof(Map).GetMethod("Notify_ThingDespawned", BindingFlags.Instance | BindingFlags.NonPublic);
                if (notifyMethod != null)
                {
                    notifyMethod.Invoke(map, new object[] { thing });
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[DeleteTarget] ForceDespawnViaReflection failed: {ex}");
                throw;
            }
        }

        private void CallInternalCleanup(Thing thing, Map map)
        {
            try
            {
                // 调用 DeSpawn 方法的不同重载
                MethodInfo despawnMethod = typeof(Thing).GetMethod("DeSpawn", new System.Type[] { });
                if (despawnMethod != null)
                {
                    despawnMethod.Invoke(thing, null);
                }

                // 确保从所有管理器中移除 - 修复版本
                if (thing is Pawn pawn)
                {
                    map.mapPawns.DeRegisterPawn(pawn); // 使用 DeRegisterPawn 而不是 RemovePawn
                }

                // 从动态绘制列表中移除
                MethodInfo notifyDespawnedMethod = typeof(Map).GetMethod("Notify_ThingDespawned", BindingFlags.Instance | BindingFlags.NonPublic);
                if (notifyDespawnedMethod != null)
                {
                    notifyDespawnedMethod.Invoke(map, new object[] { thing });
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[DeleteTarget] CallInternalCleanup failed: {ex}");
                throw;
            }
        }

        private void ShowDeleteEffect(IntVec3 cell)
        {
            Map map = parent.pawn.Map;
            
            // 使用自定义效果或默认效果
            if (Props.effectFleck != null)
            {
                FleckMaker.Static(cell, map, Props.effectFleck);
            }
            else
            {
                // 默认效果：一个明显的闪光效果
                FleckMaker.Static(cell, map, FleckDefOf.PsycastAreaEffect);
                FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), map, 2f);
                
                // 添加更多效果使其更明显
                for (int i = 0; i < 3; i++)
                {
                    FleckMaker.ThrowSmoke(cell.ToVector3Shifted() + new Vector3(Rand.Range(-0.5f, 0.5f), 0, Rand.Range(-0.5f, 0.5f)), map, 1.5f);
                }
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            // 调试模式：任何位置都有效
            return true;
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            return "强制擦除模式: 删除所有对象（包括不可销毁的）";
        }

        // 绘制预览效果 - 显示大范围区域
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            if (parent.pawn == null || parent.pawn.Map == null || !target.IsValid)
                return;

            try
            {
                // 绘制3x3的预览区域
                CellRect previewRect = CellRect.CenteredOn(target.Cell, 1);
                foreach (IntVec3 cell in previewRect)
                {
                    if (cell.InBounds(parent.pawn.Map))
                    {
                        GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Color.red, 0.8f);
                    }
                }
            }
            catch (System.Exception)
            {
                // 忽略预览绘制错误
            }
        }
    }
}
