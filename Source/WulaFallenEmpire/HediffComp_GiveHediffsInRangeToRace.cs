using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class HediffComp_GiveHediffsInRangeToRace : HediffComp
    {
        private Mote mote;

        public HediffCompProperties_GiveHediffsInRangeToRace Props => (HediffCompProperties_GiveHediffsInRangeToRace)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!parent.pawn.Awake() || parent.pawn.health == null || parent.pawn.health.InPainShock || !parent.pawn.Spawned)
            {
                return;
            }
            if (!Props.hideMoteWhenNotDrafted || parent.pawn.Drafted)
            {
                if (Props.mote != null && (mote == null || mote.Destroyed))
                {
                    mote = MoteMaker.MakeAttachedOverlay(parent.pawn, Props.mote, Vector3.zero);
                }
                if (mote != null)
                {
                    mote.Maintain();
                }
            }
            IReadOnlyList<Pawn> pawns = ((!Props.onlyPawnsInSameFaction || parent.pawn.Faction == null) ? parent.pawn.Map.mapPawns.AllPawnsSpawned : parent.pawn.Map.mapPawns.SpawnedPawnsInFaction(parent.pawn.Faction));
            foreach (Pawn pawn in pawns)
            {
                // 修改点：检查种族是否在我们的目标列表中，如果列表为空或null则不进行任何操作
                if ((Props.targetRaces.NullOrEmpty() || !Props.targetRaces.Contains(pawn.def)) || pawn.Dead || pawn.health == null || pawn == parent.pawn || !(pawn.Position.DistanceTo(parent.pawn.Position) <= Props.range) || !Props.targetingParameters.CanTarget(pawn))
                {
                    continue;
                }
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (hediff == null)
                {
                    hediff = pawn.health.AddHediff(Props.hediff, pawn.health.hediffSet.GetBrain());
                    hediff.Severity = Props.initialSeverity;
                    HediffComp_Link hediffComp_Link = hediff.TryGetComp<HediffComp_Link>();
                    if (hediffComp_Link != null)
                    {
                        hediffComp_Link.drawConnection = true;
                        hediffComp_Link.other = parent.pawn;
                    }
                }
                HediffComp_Disappears hediffComp_Disappears = hediff.TryGetComp<HediffComp_Disappears>();
                if (hediffComp_Disappears == null)
                {
                    Log.Error("HediffComp_GiveHediffsInRangeToRace has a hediff in props which does not have a HediffComp_Disappears");
                }
                else
                {
                    hediffComp_Disappears.ticksToDisappear = 5;
                }
            }
        }
    }
}