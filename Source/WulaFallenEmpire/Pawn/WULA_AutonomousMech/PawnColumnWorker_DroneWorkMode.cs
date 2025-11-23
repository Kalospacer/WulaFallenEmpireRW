using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class PawnColumnWorker_DroneWorkMode : PawnColumnWorker_Icon
    {
        protected override int Padding => 0;

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            CompAutonomousMech comp = pawn.TryGetComp<CompAutonomousMech>();
            if (comp == null || !comp.CanBeAutonomous)
            {
                return;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                Find.WindowStack.Add(new FloatMenu(DroneGizmo.GetWorkModeOptions(comp).ToList()));
            }
            base.DoCell(rect, pawn, table);
        }

        protected override Texture2D GetIconFor(Pawn pawn)
        {
            return pawn?.TryGetComp<CompAutonomousMech>()?.CurrentWorkMode?.uiIcon;
        }

        protected override string GetIconTip(Pawn pawn)
        {
            string text = pawn.TryGetComp<CompAutonomousMech>()?.CurrentWorkMode?.description;
            if (!text.NullOrEmpty())
            {
                return text;
            }
            return null;
        }
    }
}