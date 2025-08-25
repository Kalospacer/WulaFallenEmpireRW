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
            // 找到 MapPortal.Map 属性的 getter 方法
            var mapPropertyGetter = AccessTools.PropertyGetter(typeof(MapPortal), "Map");
            // 找到我们自定义的静态方法，它将返回正确的 Map
            var getShuttleMapMethod = AccessTools.Method(typeof(DialogEnterPortal_CalculateAndRecacheTransferables_Patch), nameof(GetShuttleMap));

            for (int i = 0; i < codes.Count; i++)
            {
                // 查找对 MapPortal.Map 的 get 访问
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo method && method == mapPropertyGetter)
                {
                    // 替换为调用我们的静态方法
                    yield return new CodeInstruction(OpCodes.Call, getShuttleMapMethod);
                }
                else
                {
                    yield return codes[i];
                }
            }
        }

        // 这个静态方法将由 Transpiler 注入，用于返回正确的 Map
        // 参数 portalInstance 是原始方法中对 MapPortal 实例的引用
        public static Map GetShuttleMap(MapPortal portalInstance)
        {
            if (portalInstance is ShuttlePortalAdapter adapter && adapter.shuttle != null)
            {
                return adapter.shuttle.Map;
            }
            // 如果不是我们的适配器或者 shuttle 为空，则返回原始 MapPortal 的 Map
            // 我们需要直接访问 Thing 类的 Map 属性，这是 MapPortal 继承的
            var originalMapGetter = AccessTools.PropertyGetter(typeof(Thing), "Map");
            return (Map)originalMapGetter.Invoke(portalInstance, null);
        }
    }
}