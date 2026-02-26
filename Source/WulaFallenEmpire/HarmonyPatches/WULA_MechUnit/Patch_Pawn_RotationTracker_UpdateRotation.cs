using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Pawn_RotationTracker))]
    [HarmonyPatch("UpdateRotation")]
    public static class Patch_Pawn_RotationTracker_UpdateRotation
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            // 查找 pawn.Drafted 检查并修改
            for (int i = 0; i < codes.Count; i++)
            {
                // 查找加载 pawn 并调用 get_Drafted 的代码
                if (codes[i].opcode == OpCodes.Ldarg_0 &&
                    i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Ldfld &&
                    codes[i + 1].operand is FieldInfo field && field.Name == "pawn" &&
                    i + 2 < codes.Count && codes[i + 2].opcode == OpCodes.Callvirt)
                {
                    // 检查是否是 get_Drafted 方法
                    var method = codes[i + 2].operand as MethodInfo;
                    if (method != null && method.Name == "get_Drafted")
                    {
                        // 查找随后的条件跳转
                        for (int j = i + 3; j < codes.Count; j++)
                        {
                            if (codes[j].opcode == OpCodes.Brfalse || codes[j].opcode == OpCodes.Brfalse_S)
                            {
                                // 找到条件跳转，创建新标签用于额外检查
                                var originalLabel = (Label)codes[j].operand;
                                var afterCheckLabel = il.DefineLabel();

                                // 修改原始跳转目标
                                codes[j].operand = afterCheckLabel;

                                // 在跳转指令后插入检查代码
                                var insertCodes = new List<CodeInstruction>
                                {
                                    // 加载 pawn
                                    new CodeInstruction(OpCodes.Ldarg_0),
                                    new CodeInstruction(OpCodes.Ldfld,
                                        typeof(Pawn_RotationTracker).GetField("pawn",
                                            BindingFlags.NonPublic | BindingFlags.Instance)),
                                    
                                    // 检查是否是 Wulamechunit
                                    new CodeInstruction(OpCodes.Isinst, typeof(Wulamechunit)),
                                    
                                    // 如果是 Wulamechunit，跳转到原始标签（跳过设置 Rotation）
                                    new CodeInstruction(OpCodes.Brtrue, originalLabel)
                                };

                                // 标记 afterCheckLabel
                                var labelCode = new CodeInstruction(OpCodes.Nop);
                                labelCode.labels.Add(afterCheckLabel);
                                insertCodes.Insert(0, labelCode);

                                // 插入代码
                                codes.InsertRange(j + 1, insertCodes);

                                return codes;
                            }
                        }
                    }
                }
            }

            Log.Warning("[WulaFallenEmpire] Failed to find pawn.Drafted check in UpdateRotation");
            return codes;
        }
    }
}