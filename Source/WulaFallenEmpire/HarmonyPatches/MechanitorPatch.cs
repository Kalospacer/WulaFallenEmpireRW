using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using HarmonyLib; // 引入Harmony库

namespace WulaFallenEmpire
{
    // 定义一个新的HediffCompProperties，用作标记，以赋予机械师能力
    public class HediffCompProperties_MakesMechanitor : HediffCompProperties
    {
        // 这个类本身不需要任何逻辑，它的存在就是为了在XML中被引用作为标记
        public HediffCompProperties_MakesMechanitor()
        {
            // compClass必须指向一个有效的HediffComp类。
            // 由于我们只想用这个Properties作为标记，我们可以指向一个通用的、空的HediffComp。
            // 但更简洁的方法是直接在补丁里检查Properties本身。
            // 为了避免运行时错误，我们暂时指向一个基础的HediffComp。
            // 实际上，在下面的补丁逻辑中，我们不会实例化这个compClass。
            this.compClass = typeof(HediffComp);
        }
    }


    // Harmony Patch类，用于修改MechanitorUtility.ShouldBeMechanitor方法
    [HarmonyPatch(typeof(MechanitorUtility), "ShouldBeMechanitor")]
    public static class MechanitorShouldBeMechanitorPatch
    {
        // Postfix方法将在原始方法执行后运行
        // originalResult 是原始方法的返回值
        // pawn 是原始方法的参数
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            // 如果原始方法已经返回true，则无需进一步检查
            if (__result)
            {
                return;
            }

            // 检查Biotech DLC是否激活且Pawn属于玩家安全派系
            if (ModsConfig.BiotechActive && pawn.Faction.IsPlayerSafe())
            {
                // 遍历Pawn的所有Hediff
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    // 检查Hediff的定义中是否包含HediffCompProperties_MakesMechanitor
                    if (hediff.def.comps?.Any(c => c is HediffCompProperties_MakesMechanitor) ?? false)
                    {
                        __result = true; // 如果找到，则将结果设置为true
                        return;
                    }
                }
            }
        }
    }
}
