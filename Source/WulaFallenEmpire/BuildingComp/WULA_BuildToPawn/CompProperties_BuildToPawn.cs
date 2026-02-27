// CompProperties_BuildToPawn.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_BuildToPawn : CompProperties
        {
            public PawnKindDef pawnKindDef; // 要生成的Pawn种类
            public int spawnCount = 1; // 生成数量，默认为1
            public bool inheritFaction = true; // 是否继承建筑的派系
            public bool destroyBuilding = false; // 是否销毁建筑
            public bool initDrafted = false; // 是否生成时直接设为征召

        public CompProperties_BuildToPawn()
        {
            this.compClass = typeof(CompBuildToPawn);
        }
    }
}
