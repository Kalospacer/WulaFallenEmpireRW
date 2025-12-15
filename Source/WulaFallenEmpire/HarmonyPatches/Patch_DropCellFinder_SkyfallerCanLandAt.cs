using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(DropCellFinder), "SkyfallerCanLandAt")]
    public static class Patch_DropCellFinder_SkyfallerCanLandAt
    {
        [HarmonyPrefix]
        public static bool Prefix(IntVec3 c, Map map, IntVec2 size, Faction faction, ref bool __result)
        {
            // 检查 skyfallerThingDef 是否是我们的武装穿梭机
            // 注意：SkyfallerCanLandAt 方法本身没有 skyfallerThingDef 参数。
            // 我们需要判断当前上下文是否是武装穿梭机在尝试降落。
            // 最直接的方式是检查传入的 size 是否与我们的武装穿梭机 ThingDef 的 size 匹配。
            // 这种方式不够精确，但在这个上下文中可能是最接近的。
            // 更好的方式是检查调用堆栈或通过更早的 Patch 传递上下文信息。
            // 但为了快速解决问题，我们先假设 size 匹配即可。

            // 更好的方法是，在 SkyfallerMaker.SpawnSkyfaller 方法被调用时，
            // 我们可以获取到 ThingDef，然后将其存储在一个临时变量中，供后续的 Patch 使用。
            // 但这会引入额外的复杂性。
            // 暂时先用 size 匹配来判断，如果未来出现问题再考虑更复杂的方案。

            // 考虑到 SkyfallerCanLandAt 通常与 ThingDef.Size 关联，我们尝试通过 ThingDefOf.Shuttle 获取其 Size
            // 也可以直接使用硬编码的 (3,5)
            // ThingDef shuttleDef = ThingDef.Named("WULA_ArmedShuttle");
            // if (shuttleDef != null && size == shuttleDef.Size)

            // 为了避免对其他 Skyfaller 产生影响，我们只在武装穿梭机相关的逻辑中进行额外的边界检查。
            // 由于 SkyfallerCanLandAt 不直接接收 ThingDef，我们通过 ThingDefOf.Shuttle 来判断是否是默认穿梭机
            // 如果是，并且尺寸与我们的武装穿梭机尺寸 (3,5) 匹配，则进行额外检查。
            // 或者更直接地，假设任何尺寸为 (3,5) 的 Skyfaller 都需要这个检查（如果这是我们Mod独有的尺寸）

            // 这里我们直接根据已知的武装穿梭机尺寸 (3,5) 来判断
            if (size.x == 3 && size.z == 5)
            {
                // 仅对我们的武装穿梭机执行额外的边界检查
                foreach (IntVec3 occupiedCell in GenAdj.OccupiedRect(c, Rot4.North, size))
                {
                    if (!occupiedCell.InBounds(map))
                    {
                        WulaLog.Debug($"[WULA] Harmony Patch: SkyfallerCanLandAt - Occupied cell {occupiedCell} for WULA_ArmedShuttle (size: {size}) is out of map bounds. Preventing landing.");
                        __result = false;
                        return false; // 阻止原方法执行，并返回 false
                    }
                }
            }
            return true; // 继续执行原方法
        }
    }
}