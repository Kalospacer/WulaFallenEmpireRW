using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 为Wula_PIA_Legion_Faction派系排除领袖检查错误
    /// 通过修改ShouldHaveLeader属性实现
    /// </summary>
    [HarmonyPatch(typeof(Faction), "get_ShouldHaveLeader")]
    public static class Faction_ShouldHaveLeader_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, ref bool __result)
        {
            // 对于Wula_PIA_Legion_Faction派系，强制返回false
            // 这样原代码中的检查 if (ShouldHaveLeader && leader == null) 就不会触发
            if (__instance.def?.defName == "Wula_PIA_Legion_Faction")
            {
                __result = false;
            }
        }
    }
}
