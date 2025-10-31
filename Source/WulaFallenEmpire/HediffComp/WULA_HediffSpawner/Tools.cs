using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
	public static class Tools
	{
		public static void DestroyParentHediff(Hediff parentHediff, bool debug = false)
		{
			if (parentHediff.pawn != null && parentHediff.def.defName != null && debug)
			{
				Log.Warning(parentHediff.pawn.Label + "'s Hediff: " + parentHediff.def.defName + " says goodbye.");
			}
			parentHediff.Severity = 0f;
		}

		public static float GetPawnAgeOverlifeExpectancyRatio(Pawn pawn, bool debug = false)
		{
			float result = 1f;
			if (pawn == null)
			{
				if (debug)
				{
					Log.Warning("GetPawnAgeOverlifeExpectancyRatio pawn NOT OK");
				}
				return result;
			}
			result = pawn.ageTracker.AgeBiologicalYearsFloat / pawn.RaceProps.lifeExpectancy;
			if (debug)
			{
				Log.Warning(string.Concat(new string[]
				{
					pawn.Label,
					" Age: ",
					pawn.ageTracker.AgeBiologicalYearsFloat.ToString(),
					"; lifeExpectancy: ",
					pawn.RaceProps.lifeExpectancy.ToString(),
					"; ratio:",
					result.ToString()
				}));
			}
			return result;
		}

		public static bool IsInjured(this Pawn pawn, bool debug = false)
		{
			if (pawn == null)
			{
				if (debug)
				{
					Log.Warning("pawn is null - wounded ");
				}
				return false;
			}
			float num = 0f;
			List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
			for (int i = 0; i < hediffs.Count; i++)
			{
				if (hediffs[i] is Hediff_Injury && !hediffs[i].IsPermanent())
				{
					num += hediffs[i].Severity;
				}
			}
			if (debug && num > 0f)
			{
				Log.Warning(pawn.Label + " is wounded ");
			}
			return num > 0f;
		}

		public static bool IsHungry(this Pawn pawn, bool debug = false)
		{
			if (pawn == null)
			{
				if (debug)
				{
					Log.Warning("pawn is null - IsHungry ");
				}
				return false;
			}
			bool flag = pawn.needs.food != null && pawn.needs.food.CurCategory == HungerCategory.Starving;
			if (debug && flag)
			{
				Log.Warning(pawn.Label + " is hungry ");
			}
			return flag;
		}

		public static bool OkPawn(Pawn pawn)
		{
			return pawn != null && pawn.Map != null;
		}

		public static void Warn(string warning, bool debug = false)
		{
			if (debug)
			{
				Log.Warning(warning);
			}
		}

	}
}