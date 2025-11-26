using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_PrefabSkyfallerCaller : CompProperties_SkyfallerCaller
    {
        public string prefabDefName;
        public bool freePrefab = false;

        public CompProperties_PrefabSkyfallerCaller()
        {
            compClass = typeof(CompPrefabSkyfallerCaller);
        }
    }
}