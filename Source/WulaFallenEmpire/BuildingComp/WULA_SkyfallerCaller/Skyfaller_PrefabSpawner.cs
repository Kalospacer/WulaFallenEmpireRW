using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class Skyfaller_PrefabSpawner : Skyfaller
    {
        public string prefabDefName;
        private Faction prefabFaction; // 缓存派系信息

        protected override void SpawnThings()
        {
            // Don't spawn the innerThing, we are spawning a prefab instead.
            if (string.IsNullOrEmpty(prefabDefName))
            {
                WulaLog.Debug("[Skyfaller_PrefabSpawner] prefabDefName is null or empty. Cannot spawn prefab.");
                return;
            }

            PrefabDef prefabDef = DefDatabase<PrefabDef>.GetNamed(prefabDefName, false);
            if (prefabDef == null)
            {
                WulaLog.Debug($"[Skyfaller_PrefabSpawner] Could not find PrefabDef named {prefabDefName}.");
                return;
            }

            // 获取派系信息
            Faction faction = GetPrefabFaction();

            // Correct parameter order based on compiler error: prefabDef, map, position, rotation
            PrefabUtility.SpawnPrefab(prefabDef, base.Map, base.Position, base.Rotation, faction);
        }

        private Faction GetPrefabFaction()
        {
            // 如果已经缓存了派系信息，直接返回
            if (prefabFaction != null)
                return prefabFaction;

            // 检查是否有 CompSkyfallerFaction 组件
            var factionComp = this.TryGetComp<CompSkyfallerFaction>();
            if (factionComp != null)
            {
                prefabFaction = factionComp.GetFactionForPrefab();
                return prefabFaction;
            }

            // 如果没有组件，默认使用玩家派系
            prefabFaction = Faction.OfPlayer;
            return prefabFaction;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref prefabDefName, "prefabDefName");
        }
    }
}
