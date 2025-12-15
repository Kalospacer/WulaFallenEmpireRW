using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(ThingDefGenerator_Corpses))]
    [HarmonyPatch("GenerateCorpseDef")]
    public static class WulaSpeciesCorpsePatch
    {
        /// <summary>
        /// 在生成尸体定义后，对 WulaSpecies 进行特殊处理
        /// </summary>
        [HarmonyPostfix]
        public static void ModifyWulaSpeciesCorpse(ThingDef pawnDef, ref ThingDef __result)
        {
            // 检查是否是 WulaSpecies 种族
            if (pawnDef?.defName == "WulaSpecies")
            {
                ApplyWulaSpeciesCorpseModifications(__result);
            }
        }

        /// <summary>
        /// 应用 WulaSpecies 尸体的特殊修改
        /// </summary>
        private static void ApplyWulaSpeciesCorpseModifications(ThingDef corpseDef)
        {
            if (corpseDef == null) return;

            WulaLog.Debug($"[WulaSpecies] Starting corpse modification for WulaSpecies");

            // 1. 移除腐烂组件（如果存在）
            RemoveCompProperties(corpseDef, typeof(CompProperties_Rottable));

            // 2. 移除污物生成组件（如果存在）
            RemoveCompProperties(corpseDef, typeof(CompProperties_SpawnerFilth));

            // 3. 修改可食用属性，设置为 NeverForNutrition
            if (corpseDef.ingestible != null)
            {
                corpseDef.ingestible.preferability = FoodPreferability.NeverForNutrition;
                WulaLog.Debug($"[WulaSpecies] Set ingestible preferability to NeverForNutrition");
            }

            // 4. 移除 HarbingerTreeConsumable 组件（如果存在）
            RemoveCompProperties(corpseDef, typeof(CompProperties), "CompHarbingerTreeConsumable");

            WulaLog.Debug($"[WulaSpecies] Completed corpse modification for WulaSpecies");
        }

        /// <summary>
        /// 移除指定类型的组件属性
        /// </summary>
        private static void RemoveCompProperties(ThingDef thingDef, System.Type compType, string compClassName = null)
        {
            if (thingDef.comps == null) return;

            var compsToRemove = new List<CompProperties>();
            
            foreach (var comp in thingDef.comps)
            {
                if (comp.GetType() == compType)
                {
                    compsToRemove.Add(comp);
                    WulaLog.Debug($"[WulaSpecies] Found and will remove component: {comp.GetType().Name}");
                }
                else if (!string.IsNullOrEmpty(compClassName) && comp.compClass?.Name == compClassName)
                {
                    compsToRemove.Add(comp);
                    WulaLog.Debug($"[WulaSpecies] Found and will remove component by class name: {compClassName}");
                }
            }

            foreach (var comp in compsToRemove)
            {
                thingDef.comps.Remove(comp);
                WulaLog.Debug($"[WulaSpecies] Removed component: {comp.GetType().Name}");
            }
        }
    }
}
