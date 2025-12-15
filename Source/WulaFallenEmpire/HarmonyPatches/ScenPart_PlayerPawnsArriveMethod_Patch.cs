using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod))]
    [HarmonyPatch("DoDropPods")]
    public static class ScenPart_PlayerPawnsArriveMethod_ReflectionPatch
    {
        /// <summary>
        /// 使用反射来直接调用正确的方法
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(ScenPart_PlayerPawnsArriveMethod __instance, Map map, List<Thing> startingItems)
        {
            try
            {
                // 获取私有字段 "method"
                FieldInfo methodField = typeof(ScenPart_PlayerPawnsArriveMethod).GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
                PlayerPawnsArriveMethod method = (PlayerPawnsArriveMethod)methodField.GetValue(__instance);
                // 重新创建物品分组逻辑
                List<List<Thing>> thingGroups = new List<List<Thing>>();

                foreach (Pawn startingPawn in Find.GameInitData.startingAndOptionalPawns)
                {
                    List<Thing> pawnGroup = new List<Thing>();
                    pawnGroup.Add(startingPawn);
                    thingGroups.Add(pawnGroup);
                }
                int itemIndex = 0;
                foreach (Thing startingItem in startingItems)
                {
                    if (startingItem.def.CanHaveFaction)
                    {
                        startingItem.SetFactionDirect(Faction.OfPlayer);
                    }
                    thingGroups[itemIndex].Add(startingItem);
                    itemIndex++;
                    if (itemIndex >= thingGroups.Count)
                    {
                        itemIndex = 0;
                    }
                }
                // 使用反射调用正确的 DropThingGroupsNear 方法（11个参数版本）
                MethodInfo dropMethod = typeof(DropPodUtility).GetMethod("DropThingGroupsNear", new System.Type[]
                {
                    typeof(IntVec3),
                    typeof(Map),
                    typeof(List<List<Thing>>),
                    typeof(int),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(Faction)
                });
                if (dropMethod != null)
                {
                    dropMethod.Invoke(null, new object[]
                    {
                        MapGenerator.PlayerStartSpot,
                        map,
                        thingGroups,
                        110,
                        Find.GameInitData.QuickStarted || method != PlayerPawnsArriveMethod.DropPods,
                        true,  // leaveSlag
                        true,  // canRoofPunch
                        true,  // forbid
                        false, // allowFogged
                        false, // canTransfer
                        Faction.OfPlayer  // faction
                    });
                    WulaLog.Debug("[WULA] Successfully called DropThingGroupsNear with faction parameter via reflection");
                }
                else
                {
                    WulaLog.Debug("[WULA] Could not find 11-parameter DropThingGroupsNear method");
                }
                // 返回 false 来跳过原方法的执行
                return false;
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[WULA] Error in DoDropPods prefix: {ex}");
                // 如果出错，让原方法继续执行
                return true;
            }
        }
    }
}