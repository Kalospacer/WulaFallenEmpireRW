using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public static class WulaPawnGenerationUtility
    {
        public static PawnGenerationRequest CreateNonPlayerRequest(
            PawnKindDef kind,
            Faction faction,
            Map map = null,
            bool forceGenerateNewPawn = true,
            bool allowDead = false,
            bool allowDowned = false,
            bool canGeneratePawnRelations = false,
            bool mustBeCapableOfViolence = false,
            float colonistRelationChanceFactor = 0f,
            bool forceAddFreeWarmLayerIfNeeded = false,
            bool allowGay = true,
            bool allowFood = true,
            bool allowAddictions = true)
        {
            return new PawnGenerationRequest(
                kind: kind,
                faction: faction,
                context: PawnGenerationContext.NonPlayer,
                tile: map?.Tile ?? -1,
                forceGenerateNewPawn: forceGenerateNewPawn,
                allowDead: allowDead,
                allowDowned: allowDowned,
                canGeneratePawnRelations: canGeneratePawnRelations,
                mustBeCapableOfViolence: mustBeCapableOfViolence,
                colonistRelationChanceFactor: colonistRelationChanceFactor,
                forceAddFreeWarmLayerIfNeeded: forceAddFreeWarmLayerIfNeeded,
                allowGay: allowGay,
                allowFood: allowFood,
                allowAddictions: allowAddictions
            );
        }

        public static Pawn GenerateNonPlayerPawn(
            PawnKindDef kind,
            Faction faction,
            Map map = null,
            bool forceGenerateNewPawn = true,
            bool allowDead = false,
            bool allowDowned = false,
            bool canGeneratePawnRelations = false,
            bool mustBeCapableOfViolence = false,
            float colonistRelationChanceFactor = 0f)
        {
            PawnGenerationRequest request = CreateNonPlayerRequest(
                kind,
                faction,
                map,
                forceGenerateNewPawn,
                allowDead,
                allowDowned,
                canGeneratePawnRelations,
                mustBeCapableOfViolence,
                colonistRelationChanceFactor
            );

            return PawnGenerator.GeneratePawn(request);
        }

        public static void PrepareForThingOwner(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (pawn.Spawned)
                pawn.DeSpawnOrDeselect();

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            pawn.pather?.StopDead();
            pawn.jobs?.StopAll();
        }
    }
}
