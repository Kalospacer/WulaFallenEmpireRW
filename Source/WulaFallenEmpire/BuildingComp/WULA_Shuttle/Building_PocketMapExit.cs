using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_PocketMapExit : MapPortal
    {
        public Building_ArmedShuttleWithPocket parentShuttle;

        private CompPocketMapPortal Portal => parentShuttle?.PocketPortal;

        public override string EnterString => "WULA.PocketSpace.ExitToMainMap".Translate();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref parentShuttle, "parentShuttle");
        }

        public override bool IsEnterable(out string reason)
        {
            if (parentShuttle == null || !parentShuttle.Spawned)
            {
                reason = "WULA.PocketSpace.ShuttleNotDocked".Translate();
                return false;
            }

            reason = null;
            return true;
        }

        public override Map GetOtherMap()
        {
            return parentShuttle?.Map;
        }

        public override IntVec3 GetDestinationLocation()
        {
            return parentShuttle?.InteractionCell ?? IntVec3.Invalid;
        }

        public override void OnEntered(Pawn pawn)
        {
            Notify_ThingAdded(pawn);
            Portal?.NotifyPawnTransferred();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (parentShuttle != null && parentShuttle.Spawned)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CommandViewSurface".Translate(),
                    defaultDesc = "CommandViewSurfaceDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave"),
                    action = () => CameraJumper.TryJumpAndSelect(parentShuttle)
                };
            }
        }
    }
}
