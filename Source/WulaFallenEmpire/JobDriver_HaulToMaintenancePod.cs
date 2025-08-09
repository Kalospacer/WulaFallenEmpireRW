using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_HaulToMaintenancePod : JobDriver
    {
        private const TargetIndex PatientIndex = TargetIndex.A;
        private const TargetIndex PodIndex = TargetIndex.B;

        protected Pawn Patient => (Pawn)job.GetTarget(PatientIndex).Thing;
        protected Building_Bed Pod => (Building_Bed)job.GetTarget(PodIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed) &&
                   pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PodIndex);
            this.FailOnDespawnedNullOrForbidden(PatientIndex);
            this.FailOnAggroMentalState(PatientIndex);
            this.FailOn(() => !Patient.Downed);

            var podComp = Pod.TryGetComp<CompMaintenancePod>();
            this.FailOn(() => podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn);

            // Go to the patient
            yield return Toils_Goto.GotoThing(PatientIndex, PathEndMode.OnCell);

            // Pick up the patient
            yield return Toils_Haul.StartCarryThing(PatientIndex);

            // Carry the patient to the pod
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // Place the patient in the pod
            yield return new Toil
            {
                initAction = () =>
                {
                    podComp.StartCycle(Patient);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}