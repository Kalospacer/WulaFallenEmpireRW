using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class FloatMenuProvider_Mech : FloatMenuOptionProvider
{
	protected override bool Drafted => true;

	protected override bool Undrafted => true;

	protected override bool Multiselect => false;

	protected override bool MechanoidCanDo => true;

	public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
	{
		return base.SelectedPawnValid(pawn, context) && pawn.HasComp<CompMechWeapon>();
	}

	public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
	{
		Pawn pawn = context.FirstSelectedPawn;
		if (clickedThing.def.IsWeapon && pawn.CanReserveAndReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
		{
			yield return new FloatMenuOption("Equip".Translate(clickedThing.Label), delegate
			{
				pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Equip, clickedThing));
			});
		}
	}
}
}