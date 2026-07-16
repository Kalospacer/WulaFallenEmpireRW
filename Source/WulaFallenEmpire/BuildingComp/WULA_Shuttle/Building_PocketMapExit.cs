using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_PocketMapExit : MapPortal
    {
        private static readonly CachedTexture ExitMapTex = new CachedTexture("UI/Commands/ExitCave");
        private static readonly CachedTexture CancelEnterTex = new CachedTexture("UI/Designators/Cancel");
        private static readonly CachedTexture ViewSurfaceTex = new CachedTexture("UI/Commands/ViewCave");

        public Building_ArmedShuttleWithPocket parentShuttle;

        private CompPocketMapPortal Portal => parentShuttle?.PocketPortal;

        public override string EnterString => "WULA.PocketSpace.ExitToMainMap".Translate();

        public override string CancelEnterString => "CommandCancelExitPortal".Translate();

        protected override Texture2D EnterTex => ExitMapTex.Texture;

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
            // Do not call MapPortal.GetGizmos(): it reads def.portal (null on this def) and adds a
            // pocket-map view button that does not apply to a shuttle exit.
            yield return new Command_Action
            {
                action = () => Find.WindowStack.Add(new Dialog_EnterPortal(this)),
                icon = ExitMapTex.Texture,
                defaultLabel = EnterString + "...",
                defaultDesc = "WULA.PocketSpace.ExitToMainMapDesc".Translate(),
                Disabled = !IsEnterable(out string reason),
                disabledReason = reason
            };

            if (LoadInProgress)
            {
                yield return new Command_Action
                {
                    action = CancelLoad,
                    icon = CancelEnterTex.Texture,
                    defaultLabel = CancelEnterString,
                    defaultDesc = "CommandCancelEnterPortalDesc".Translate()
                };
            }

            if (parentShuttle != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CommandViewSurface".Translate(),
                    defaultDesc = "CommandViewSurfaceDesc".Translate(),
                    icon = ViewSurfaceTex.Texture,
                    action = () =>
                    {
                        if (parentShuttle.Spawned)
                        {
                            CameraJumper.TryJumpAndSelect(parentShuttle);
                        }
                    },
                    Disabled = !parentShuttle.Spawned,
                    disabledReason = "WULA.PocketSpace.ShuttleNotDocked".Translate()
                };
            }
        }
    }
}
