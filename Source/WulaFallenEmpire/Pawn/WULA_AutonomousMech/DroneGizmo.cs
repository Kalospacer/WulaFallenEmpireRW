using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class DroneGizmo : Gizmo
    {
        private CompAutonomousMech comp;
        private HashSet<CompAutonomousMech> groupedComps;

        private static readonly Texture2D BarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.34f, 0.42f, 0.43f));
        private static readonly Texture2D BarHighlightTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.43f, 0.54f, 0.55f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.03f, 0.035f, 0.05f));
        // private static readonly Texture2D DragBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.74f, 0.97f, 0.8f));

        // private static bool draggingBar;

        public DroneGizmo(CompAutonomousMech comp)
        {
            this.comp = comp;
        }

        public override float GetWidth(float maxWidth)
        {
            return 160f;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect rect2 = rect.ContractedBy(10f);
            Widgets.DrawWindowBackground(rect);

            string text = "WULA_AutonomousMech".Translate();
            Rect rect3 = new Rect(rect2.x, rect2.y, rect2.width, Text.CalcHeight(text, rect2.width) + 8f);
            Text.Font = GameFont.Small;
            Widgets.Label(rect3, text);

            Rect rect4 = new Rect(rect2.x, rect3.yMax, rect2.width, rect2.height - rect3.height);
            DraggableBarForGroup(rect4);

            Text.Anchor = TextAnchor.MiddleCenter;
            string energyText = comp.GetEnergyLevel().ToStringPercent();
            Widgets.Label(rect4, energyText);
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(rect4, () => "WULA_EnergyInfo".Translate(energyText), Gen.HashCombineInt(comp.GetHashCode(), 34242419));

            // Work Mode Button
            Rect rect6 = new Rect(rect2.x + rect2.width - 24f, rect2.y, 24f, 24f);
            if (Widgets.ButtonImageFitted(rect6, comp.CurrentWorkMode?.uiIcon ?? BaseContent.BadTex))
            {
                Find.WindowStack.Add(new FloatMenu(GetWorkModeOptions(comp, groupedComps).ToList()));
            }
            TooltipHandler.TipRegion(rect6, "WULA_Switch_Mech_WorkMode".Translate());
            Widgets.DrawHighlightIfMouseover(rect6);

            return new GizmoResult(GizmoState.Clear);
        }

        private void DraggableBarForGroup(Rect rect)
        {
            // We are not actually dragging the energy level, but maybe a threshold?
            // For now, just display the energy level.
            // If we want to set recharge threshold, we need a property in CompAutonomousMech for that.
            // Assuming we want to visualize energy level:
            
            Widgets.FillableBar(rect, comp.GetEnergyLevel(), BarTex, EmptyBarTex, false);
        }

        public static IEnumerable<FloatMenuOption> GetWorkModeOptions(CompAutonomousMech comp, HashSet<CompAutonomousMech> groupedComps = null)
        {
            foreach (DroneWorkModeDef mode in DefDatabase<DroneWorkModeDef>.AllDefs.OrderBy(d => d.uiOrder))
            {
                yield return new FloatMenuOption(mode.LabelCap, delegate
                {
                    comp.SetWorkMode(mode);
                    if (groupedComps != null)
                    {
                        foreach (CompAutonomousMech groupedComp in groupedComps)
                        {
                            groupedComp.SetWorkMode(mode);
                        }
                    }
                }, mode.uiIcon, Color.white);
            }
        }

        public override bool GroupsWith(Gizmo other)
        {
            return other is DroneGizmo;
        }

        public override void MergeWith(Gizmo other)
        {
            base.MergeWith(other);
            if (other is DroneGizmo droneGizmo)
            {
                if (groupedComps == null)
                {
                    groupedComps = new HashSet<CompAutonomousMech>();
                }
                groupedComps.Add(droneGizmo.comp);
                if (droneGizmo.groupedComps != null)
                {
                    groupedComps.AddRange(droneGizmo.groupedComps);
                }
            }
        }
    }
}