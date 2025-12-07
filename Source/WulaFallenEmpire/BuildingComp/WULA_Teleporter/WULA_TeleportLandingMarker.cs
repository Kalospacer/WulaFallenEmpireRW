using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class WULA_TeleportLandingMarker : Thing
    {
        public Thing sourceThing;
        
        public CompMapTeleporter SourceTeleporter => sourceThing?.TryGetComp<CompMapTeleporter>();

        public override CellRect? CustomRectForSelector
        {
            get
            {
                if (SourceTeleporter != null)
                {
                    return CellRect.CenteredOn(Position, SourceTeleporter.Props.areaSize.x, SourceTeleporter.Props.areaSize.z);
                }
                return null;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref sourceThing, "sourceThing");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = "WULA_ConfirmTeleport".Translate(),
                defaultDesc = "WULA_ConfirmTeleportDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip"),
                action = Confirm
            };

            yield return new Command_Action
            {
                defaultLabel = "WULA_MoveMarker".Translate(),
                defaultDesc = "WULA_MoveMarkerDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Install"),
                action = StartMove
            };
        }

        private void StartMove()
        {
            if (SourceTeleporter != null)
            {
                Find.DesignatorManager.Select(new Designator_TeleportArrival(SourceTeleporter, Map, this));
            }
        }

        private void Confirm()
        {
            if (SourceTeleporter != null)
            {
                SourceTeleporter.ConfirmArrival(Position, Map);
            }
            Destroy();
        }
        
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (SourceTeleporter != null)
            {
                GenDraw.DrawFieldEdges(CellRect.CenteredOn(Position, SourceTeleporter.Props.areaSize.x, SourceTeleporter.Props.areaSize.z).Cells.ToList());
            }
        }
    }
}