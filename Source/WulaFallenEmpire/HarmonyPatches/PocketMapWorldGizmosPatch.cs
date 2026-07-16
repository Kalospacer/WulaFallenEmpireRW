using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public static class PocketMapWorldGizmoUtility
    {
        private static readonly Texture2D ViewTex = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_View_ArmedShuttle_Pocket");

        public static IEnumerable<Gizmo> AppendViewGizmo(IEnumerable<Gizmo> original, Thing shuttleThing)
        {
            foreach (Gizmo gizmo in original)
            {
                yield return gizmo;
            }

            CompPocketMapPortal portal = shuttleThing?.TryGetComp<CompPocketMapPortal>();
            if (portal?.PocketMap != null)
            {
                yield return new Command_Action
                {
                    icon = ViewTex,
                    defaultLabel = "WULA.PocketSpace.ViewMap".Translate(),
                    defaultDesc = "WULA.PocketSpace.ViewMapDesc".Translate(),
                    action = portal.ViewPocketMap
                };
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Caravan_PocketMapGizmos_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = PocketMapWorldGizmoUtility.AppendViewGizmo(__result, __instance.Shuttle);
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetGizmos))]
    public static class WorldObject_PocketMapGizmos_Patch
    {
        private static readonly FieldInfo TransportersField = AccessTools.Field(
            typeof(TravellingTransporters), "transporters");

        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!(__instance is TravellingTransporters travelling))
            {
                return;
            }

            List<ActiveTransporterInfo> transporters =
                TransportersField.GetValue(travelling) as List<ActiveTransporterInfo>;
            Thing shuttle = transporters?.Count > 0 ? transporters[0].GetShuttle() : null;
            __result = PocketMapWorldGizmoUtility.AppendViewGizmo(__result, shuttle);
        }
    }
}
