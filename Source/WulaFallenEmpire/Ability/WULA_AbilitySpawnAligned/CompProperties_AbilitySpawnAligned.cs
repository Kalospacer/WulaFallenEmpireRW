using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilitySpawnAligned : CompProperties_AbilitySpawn
    {
        // 是否将生成的物品与施法者阵营对齐
        public bool alignFaction = true;

        public CompProperties_AbilitySpawnAligned()
        {
            compClass = typeof(CompAbilityEffect_SpawnAligned);
        }
    }
}
