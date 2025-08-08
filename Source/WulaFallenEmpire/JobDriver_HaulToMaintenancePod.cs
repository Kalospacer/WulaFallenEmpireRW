using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_HaulToMaintenancePod : JobDriver
    {
        private const TargetIndex ComponentInd = TargetIndex.A;
        private const TargetIndex PodInd = TargetIndex.B;

        protected Thing Component => job.GetTarget(ComponentInd).Thing;
        protected Building_Storage Pod => (Building_Storage)job.GetTarget(PodInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Component, job, 1, -1, null, errorOnFailed) && pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PodInd);
            this.FailOnBurningImmobile(PodInd);

            yield return Toils_Goto.GotoThing(ComponentInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ComponentInd);

            yield return Toils_Haul.StartCarryThing(ComponentInd, false, true, false)
                .FailOnDestroyedNullOrForbidden(ComponentInd);

            yield return Toils_Goto.GotoThing(PodInd, PathEndMode.Touch);

            Toil findPlaceAndDrop = new Toil();
            findPlaceAndDrop.initAction = delegate
            {
                Pawn actor = findPlaceAndDrop.actor;
                Job curJob = actor.jobs.curJob;
                Thing carriedThing = curJob.GetTarget(ComponentInd).Thing;
                
                CompMaintenancePod podComp = curJob.GetTarget(PodInd).Thing.TryGetComp<CompMaintenancePod>();

                if (podComp != null)
                {
                    podComp.AddComponents(carriedThing);
                }
                else
                {
                    // Fallback if something goes wrong, just drop it near the pod
                    actor.carryTracker.TryDropCarriedThing(Pod.Position, ThingPlaceMode.Near, out Thing _);
                }
            };
            yield return findPlaceAndDrop;
        }
    }
}