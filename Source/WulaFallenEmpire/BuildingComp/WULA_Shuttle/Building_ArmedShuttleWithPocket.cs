using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class Building_ArmedShuttleWithPocket : Building_ArmedShuttle
    {
        public CompPocketMapPortal PocketPortal => GetComp<CompPocketMapPortal>();

        public Map PocketMap => PocketPortal?.PocketMap;

        public bool PocketMapGenerated => PocketMap != null;

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            yield return PocketPortal.GetEnterFloatMenuOption(Gen.YieldSingle(selPawn));
        }

        public override IEnumerable<FloatMenuOption> GetMultiSelectFloatMenuOptions(IEnumerable<Pawn> selPawns)
        {
            foreach (FloatMenuOption option in base.GetMultiSelectFloatMenuOptions(selPawns))
            {
                yield return option;
            }

            yield return PocketPortal.GetEnterFloatMenuOption(selPawns);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!Destroyed && Spawned)
            {
                PocketPortal?.EjectAndDestroyPocketMap(Map, InteractionCell);
            }

            base.Destroy(mode);
        }
    }
}
