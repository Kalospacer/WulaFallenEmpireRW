using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld; // Added for AbilityDef

namespace WulaFallenEmpire
{
    public class JobDriver_CastEmergencyEnergyRestore : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return new Toil
            {
                initAction = delegate
                {
                    var ability = pawn.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("WULA_EmergencyEnergyRestore"));
                    if (ability != null)
                    {
                        ability.Activate(pawn.Position, pawn);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
