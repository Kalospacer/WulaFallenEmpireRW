using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompPrefabSkyfallerCaller : CompSkyfallerCaller
    {
        private CompProperties_PrefabSkyfallerCaller PropsPrefab => (CompProperties_PrefabSkyfallerCaller)props;

        private List<ThingDefCountClass> _cachedCostList;

        protected override List<ThingDefCountClass> CostList
        {
            get
            {
                if (!string.IsNullOrEmpty(PropsPrefab.prefabDefName))
                {
                    if (PropsPrefab.freePrefab)
                    {
                        return null;
                    }
                    if (_cachedCostList == null)
                    {
                        _cachedCostList = CalculatePrefabCost();
                    }
                    return _cachedCostList;
                }
                return base.CostList;
            }
        }

        private List<ThingDefCountClass> CalculatePrefabCost()
        {
            var prefab = DefDatabase<PrefabDef>.GetNamed(PropsPrefab.prefabDefName, false);
            if (prefab == null)
            {
                WulaLog.Debug($"[PrefabSkyfallerCaller] Could not find PrefabDef named {PropsPrefab.prefabDefName}");
                return new List<ThingDefCountClass>(); // Return empty list to avoid null reference
            }

            var totalCost = new Dictionary<ThingDef, int>();
            foreach (var (thingData, _, _) in PrefabUtility.GetThings(prefab, IntVec3.Zero, Rot4.North))
            {
                if (thingData.def.costList != null)
                {
                    foreach (var cost in thingData.def.costList)
                    {
                        if (totalCost.ContainsKey(cost.thingDef))
                        {
                            totalCost[cost.thingDef] += cost.count;
                        }
                        else
                        {
                            totalCost.Add(cost.thingDef, cost.count);
                        }
                    }
                }
            }
            return totalCost.Select(kvp => new ThingDefCountClass(kvp.Key, kvp.Value)).ToList();
        }

        protected override void ExecuteSkyfallerCall()
        {
            if (!string.IsNullOrEmpty(PropsPrefab.prefabDefName))
            {
                // Final material check before launching
                if (!HasEnoughMaterials())
                {
                    WulaLog.Debug($"[PrefabSkyfallerCaller] Aborting skyfaller call due to insufficient materials at the last moment.");
                    ResetCall(); // Reset the calling state
                    return;
                }

                ConsumeMaterials();

                SpawnPrefabSkyfaller();
            }
            else
            {
                base.ExecuteSkyfallerCall();
            }
        }

        // 新增：重写自动呼叫方法
        protected override void ExecuteAutoSkyfallerCall()
        {
            if (!string.IsNullOrEmpty(PropsPrefab.prefabDefName))
            {
                // 非玩家派系自动呼叫不需要资源检查
                HandleRoofDestruction();

                SpawnPrefabSkyfaller();

                ResetCall();
                autoCallScheduled = false;
            }
            else
            {
                base.ExecuteAutoSkyfallerCall();
            }
        }

        // 新增：统一的 Prefab Skyfaller 生成方法
        private void SpawnPrefabSkyfaller()
        {
            Thing thing = ThingMaker.MakeThing(Props.skyfallerDef);
            if (thing is Skyfaller_PrefabSpawner skyfaller)
            {
                skyfaller.prefabDefName = PropsPrefab.prefabDefName;
                GenSpawn.Spawn(skyfaller, parent.Position, parent.Map);
            }
            else
            {
                WulaLog.Debug($"[PrefabSkyfallerCaller] Failed to create Skyfaller_PrefabSpawner. Created thing is of type {thing.GetType().FullName}. Def: {Props.skyfallerDef.defName}, ThingClass: {Props.skyfallerDef.thingClass.FullName}");
                // Fallback: spawn as normal skyfaller if possible, or just abort
                if (thing is Skyfaller normalSkyfaller)
                {
                     GenSpawn.Spawn(normalSkyfaller, parent.Position, parent.Map);
                }
            }

            if (PropsPrefab.destroyBuilding && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        // 新增：重写屋顶处理方法以确保日志输出
        private void HandleRoofDestruction()
        {
            if (parent?.Map == null) return;
            
            IntVec3 targetPos = parent.Position;
            RoofDef roof = targetPos.GetRoof(parent.Map);
            
            if (roof != null && !roof.isThickRoof && Props.allowThinRoof)
            {
                WulaLog.Debug($"[PrefabSkyfallerCaller] Destroying thin roof at {targetPos}");
                parent.Map.roofGrid.SetRoof(targetPos, null);
                
                // 生成屋顶破坏效果
                FleckMaker.ThrowDustPuffThick(targetPos.ToVector3Shifted(), parent.Map, 2f, new Color(1f, 1f, 1f, 2f));
            }
        }

        // 新增：调试信息
        //public override string CompInspectStringExtra()
        //{
        //    var baseString = base.CompInspectStringExtra();
            
        //    if (!string.IsNullOrEmpty(PropsPrefab.prefabDefName))
        //    {
        //        var sb = new System.Text.StringBuilder();
        //        if (!string.IsNullOrEmpty(baseString))
        //        {
        //            sb.AppendLine(baseString);
        //        }
        //        sb.Append($"Prefab: {PropsPrefab.prefabDefName}");
        //        return sb.ToString();
        //    }
            
        //    return baseString;
        //}
    }
}
