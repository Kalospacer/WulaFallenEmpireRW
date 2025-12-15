using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_GlobalWorkTable : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("WULA_WeaponArmor_Productor"));
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_GlobalWorkTable table) || !table.Spawned || table.IsForbidden(pawn))
            {
                if (forced) WulaLog.Debug($"[WULA_DEBUG] HasJobOnThing: Target invalid or forbidden. {t}");
                return false;
            }

            if (!pawn.CanReserve(table, 1, -1, null, forced))
            {
                if (forced) WulaLog.Debug($"[WULA_DEBUG] HasJobOnThing: Cannot reserve table.");
                return false;
            }

            // 检查是否有需要收集材料的订单
            var order = table.globalOrderStack.orders.FirstOrDefault(o => o.state == GlobalProductionOrder.ProductionState.Gathering && !o.paused);
            if (order == null)
            {
                if (forced)
                {
                    WulaLog.Debug($"[WULA_DEBUG] HasJobOnThing: No gathering order found. Total orders: {table.globalOrderStack.orders.Count}");
                    foreach (var o in table.globalOrderStack.orders)
                    {
                        WulaLog.Debug($"  - Order: {o.Label}, State: {o.state}, Paused: {o.paused}");
                    }
                }
                return false;
            }

            // 检查是否已经有足够的材料在容器中或云端
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            var neededMaterials = GetNeededMaterials(order, table, globalStorage);
            if (neededMaterials.Count == 0)
            {
                if (forced) WulaLog.Debug($"[WULA_DEBUG] HasJobOnThing: Order has enough resources.");
                return false;
            }

            // 查找所需材料
            var ingredients = FindBestIngredients(pawn, table, neededMaterials);
            if (ingredients == null)
            {
                if (forced) WulaLog.Debug($"[WULA_DEBUG] HasJobOnThing: Could not find ingredients for {order.Label}.");
                return false;
            }
            
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_GlobalWorkTable table))
                return null;

            var order = table.globalOrderStack.orders.FirstOrDefault(o => o.state == GlobalProductionOrder.ProductionState.Gathering && !o.paused);
            if (order == null)
                return null;

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            var neededMaterials = GetNeededMaterials(order, table, globalStorage);
            if (neededMaterials.Count == 0) return null;

            var ingredients = FindBestIngredients(pawn, table, neededMaterials);
            if (ingredients == null)
                return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_HaulToGlobalWorkTable"), t);
            job.targetQueueB = ingredients.Select(i => new LocalTargetInfo(i.Key)).ToList();
            job.countQueue = ingredients.Select(i => i.Value).ToList();
            return job;
        }

        private List<KeyValuePair<Thing, int>> FindBestIngredients(Pawn pawn, Building_GlobalWorkTable table, Dictionary<ThingDef, int> neededMaterials)
        {
            var result = new List<KeyValuePair<Thing, int>>();

            WulaLog.Debug($"[WULA_DEBUG] Needed materials: {string.Join(", ", neededMaterials.Select(k => $"{k.Key.defName} x{k.Value}"))}");

            foreach (var kvp in neededMaterials)
            {
                ThingDef def = kvp.Key;
                int countNeeded = kvp.Value;

                // 在地图上查找材料
                // 注意：t.IsInAnyStorage() 可能会过滤掉放在地上的材料，如果玩家没有设置储存区
                // 为了测试，先移除 IsInAnyStorage 限制，或者确保测试时材料在储存区
                var things = pawn.Map.listerThings.ThingsOfDef(def)
                    .Where(t => !t.IsForbidden(pawn) && pawn.CanReserve(t)) // 移除了 IsInAnyStorage() 以放宽条件
                    .OrderBy(t => t.Position.DistanceTo(pawn.Position))
                    .ToList();

                int currentCount = 0;
                foreach (var thing in things)
                {
                    int take = UnityEngine.Mathf.Min(thing.stackCount, countNeeded - currentCount);
                    if (take > 0)
                    {
                        result.Add(new KeyValuePair<Thing, int>(thing, take));
                        currentCount += take;
                        if (currentCount >= countNeeded) break;
                    }
                }
                
                WulaLog.Debug($"[WULA_DEBUG] Found {currentCount}/{countNeeded} of {def.defName}");
            }

            return result.Count > 0 ? result : null;
        }

        private Dictionary<ThingDef, int> GetNeededMaterials(GlobalProductionOrder order, Building_GlobalWorkTable table, GlobalStorageWorldComponent storage)
        {
            var needed = new Dictionary<ThingDef, int>();
            
            // 1. 计算总需求
            var totalRequired = order.GetProductCostList();
            if (totalRequired.Count == 0)
            {
                totalRequired = new Dictionary<ThingDef, int>();

                // 处理配方原料 (Ingredients) - 简化处理，假设配方只使用固定材料
                // 实际情况可能更复杂，需要处理过滤器
                foreach (var ingredient in order.recipe.ingredients)
                {
                    // 这里简化：只取第一个允许的物品作为需求
                    // 更好的做法是动态匹配，但这需要更复杂的逻辑
                    var def = ingredient.filter.AllowedThingDefs.FirstOrDefault();
                    if (def == null) continue;

                    int count = ingredient.CountRequiredOfFor(def, order.recipe);
                    if (count <= 0) continue;

                    if (totalRequired.ContainsKey(def)) totalRequired[def] += count;
                    else totalRequired[def] = count;
                }
            }

            // 2. 减去云端已有的
            foreach (var kvp in totalRequired)
            {
                int cloudCount = storage?.GetInputStorageCount(kvp.Key) ?? 0;
                int remaining = kvp.Value - cloudCount;
                
                // 3. 减去工作台容器中已有的
                int containerCount = table.innerContainer.TotalStackCountOfDef(kvp.Key);
                remaining -= containerCount;

                // 4. 减去轨道贸易信标（已通电）的范围内已有的
                remaining -= CountInPoweredTradeBeaconRange(table.Map, kvp.Key);

                if (remaining > 0)
                {
                    needed[kvp.Key] = remaining;
                }
            }

            return needed;
        }

        private int CountInPoweredTradeBeaconRange(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;

            int count = 0;
            foreach (var beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (var cell in beacon.TradeableCells)
                {
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (t != null && t.def == def)
                        {
                            count += t.stackCount;
                        }
                    }
                }
            }
            return count;
        }
    }
}
