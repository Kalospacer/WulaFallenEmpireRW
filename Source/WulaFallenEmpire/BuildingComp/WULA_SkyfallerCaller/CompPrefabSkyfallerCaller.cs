using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

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
                Log.Error($"[PrefabSkyfallerCaller] Could not find PrefabDef named {PropsPrefab.prefabDefName}");
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
                    Log.Warning($"[PrefabSkyfallerCaller] Aborting skyfaller call due to insufficient materials at the last moment.");
                    ResetCall(); // Reset the calling state
                    return;
                }

                ConsumeMaterials();

                Thing thing = ThingMaker.MakeThing(Props.skyfallerDef);
                if (thing is Skyfaller_PrefabSpawner skyfaller)
                {
                    skyfaller.prefabDefName = PropsPrefab.prefabDefName;
                    GenSpawn.Spawn(skyfaller, parent.Position, parent.Map);
                }
                else
                {
                    Log.Error($"[PrefabSkyfallerCaller] Failed to create Skyfaller_PrefabSpawner. Created thing is of type {thing.GetType().FullName}. Def: {Props.skyfallerDef.defName}, ThingClass: {Props.skyfallerDef.thingClass.FullName}");
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
            else
            {
                base.ExecuteSkyfallerCall();
            }
        }
    }
}