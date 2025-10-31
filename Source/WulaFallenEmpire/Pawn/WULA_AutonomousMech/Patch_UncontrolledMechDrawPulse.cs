using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;
using UnityEngine;

namespace WulaFallenEmpire
{
    // 修复红色名字问题 - 直接修补 PawnNameColorUtility.PawnNameColorOf 方法
    [HarmonyPatch(typeof(PawnNameColorUtility), "PawnNameColorOf")]
    public static class Patch_PawnNameColorUtility_PawnNameColorOf
    {
        public static void Postfix(Pawn pawn, ref Color __result)
        {
            if (pawn != null && pawn.IsColonyMech)
            {
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 使用正常的白色名字
                    __result = Color.white;
                }
            }
        }
    }
}
