using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class Skyfaller_PrefabSpawner : Skyfaller
    {
        public string prefabDefName;

        protected override void SpawnThings()
        {
            // Don't spawn the innerThing, we are spawning a prefab instead.
            if (string.IsNullOrEmpty(prefabDefName))
            {
                Log.Error("[Skyfaller_PrefabSpawner] prefabDefName is null or empty. Cannot spawn prefab.");
                return;
            }

            PrefabDef prefabDef = DefDatabase<PrefabDef>.GetNamed(prefabDefName, false);
            if (prefabDef == null)
            {
                Log.Error($"[Skyfaller_PrefabSpawner] Could not find PrefabDef named {prefabDefName}.");
                return;
            }

            // Correct parameter order based on compiler error: prefabDef, map, position, rotation
            PrefabUtility.SpawnPrefab(prefabDef, base.Map, base.Position, base.Rotation);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref prefabDefName, "prefabDefName");
        }
    }
}