using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_PrefabSpawner : CompProperties
    {
        public string prefabDefName;
        public bool consumesMaterials = true;

        public CompProperties_PrefabSpawner()
        {
            compClass = typeof(CompPrefabSpawner);
        }
    }
}