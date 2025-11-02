using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn), "get_IsColonyMechPlayerControlled")]
    public static class Patch_IsColonyMechPlayerControlled
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            // 如果原版已经返回true，不需要修改
            if (__result)
                return;

            // 检查是否是殖民地机械
            if (!__instance.IsColonyMech)
                return;

            // 检查是否有自主机械组件
            var comp = __instance.GetComp<CompAutonomousMech>();
            if (comp == null)
                return;

            // 如果机械族处于自主战斗模式，则视为玩家控制
            if (comp.CanFightAutonomously)
            {
                __result = true;
                return;
            }

            // 如果机械族处于自主工作模式，也视为玩家控制（用于工作相关判定）
            if (comp.CanWorkAutonomously)
            {
                __result = true;
                return;
            }
        }
    }
}
