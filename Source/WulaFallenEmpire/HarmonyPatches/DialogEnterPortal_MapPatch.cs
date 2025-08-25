using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Dialog_EnterPortal), "CalculateAndRecacheTransferables")]
    public static class DialogEnterPortal_CalculateAndRecacheTransferables_Patch
    {
        // Transpiler 负责修改方法的 IL 代码
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            // 找到 Thing.Map 属性的 getter 方法 (MapPortal 继承自 Thing)
            var mapPropertyGetter = AccessTools.PropertyGetter(typeof(Verse.Thing), "Map");
            // 找到我们自定义的静态方法，它将返回正确的 Map
            var getShuttleMapMethod = AccessTools.Method(typeof(DialogEnterPortal_CalculateAndRecacheTransferables_Patch), nameof(GetShuttleMap));

            Log.Message("[WULA-DEBUG] Transpiler for CalculateAndRecacheTransferables started.");

            for (int i = 0; i < codes.Count; i++)
            {
                // 查找对 Thing.Map 的 get 访问
                if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo method && method == mapPropertyGetter)
                {
                    Log.Message($"[WULA-DEBUG] Transpiler found Thing.Map getter at index {i}.");
                    // 替换为调用我们的静态方法
                    yield return new CodeInstruction(OpCodes.Call, getShuttleMapMethod);
                }
                else
                {
                    yield return codes[i];
                }
            }
            Log.Message("[WULA-DEBUG] Transpiler for CalculateAndRecacheTransferables finished.");
        }

        // 这个静态方法将由 Transpiler 注入，用于返回正确的 Map
        // 参数 portalInstance 是原始方法中对 MapPortal 实例的引用
        public static Map GetShuttleMap(MapPortal portalInstance)
        {
            if (portalInstance is ShuttlePortalAdapter adapter)
            {
                Log.Message($"[WULA-DEBUG] portalInstance is ShuttlePortalAdapter. adapter.shuttle: {adapter.shuttle?.def.defName ?? "null"}");
                if (adapter.shuttle != null)
                {
                    // 确保 adapter.shuttle.Map 不为 null
                    if (adapter.shuttle.Map != null)
                    {
                        return adapter.shuttle.Map;
                    }
                    else
                    {
                        Log.Error($"[WULA] Shuttle {adapter.shuttle.def.defName} is not spawned on any map when trying to get its map.");
                        return null; // 返回 null，让后续代码处理
                    }
                }
            }
            
            // 如果不是我们的适配器，或者适配器中的 shuttle 为空，
            // 则尝试获取原始 MapPortal 的 Map。
            // 这里需要非常小心，因为 portalInstance 本身也可能是 null，
            // 或者它继承自 Thing 的 Map 属性是 null。
            if (portalInstance == null)
            {
                Log.Error("[WULA] GetShuttleMap received a null portalInstance.");
                return null;
            }

            var originalMapGetter = AccessTools.PropertyGetter(typeof(Thing), "Map");
            if (originalMapGetter == null)
            {
                Log.Error("[WULA] Could not get Thing.Map getter via AccessTools.");
                return null;
            }

            Map result = null;
            try
            {
                result = (Map)originalMapGetter.Invoke(portalInstance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error invoking original Thing.Map getter: {ex.Message}");
            }
            
            Log.Message($"[WULA-DEBUG] GetShuttleMap returning original Map. Result: {result?.ToString() ?? "null"}");
            return result;
        }
    }
}