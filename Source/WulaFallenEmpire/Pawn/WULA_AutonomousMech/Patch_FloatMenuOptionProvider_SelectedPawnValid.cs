using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), "SelectedPawnValid")]
    public static class Patch_FloatMenuOptionProvider_SelectedPawnValid
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, FloatMenuContext context, ref bool __result)
        {
            // 如果已经有效，不需要修改
            if (__result)
                return;

            // 检查是否是机械族且被原版逻辑拒绝
            if (!pawn.RaceProps.IsMechanoid)
                return;

            // 检查是否有自主机械组件
            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp == null || !comp.CanWorkAutonomously)
                return;

            // 对于自主机械族，直接返回true，跳过机械族限制
            // 其他条件已经在原版方法中检查过了
            __result = true;
        }
    }
}
