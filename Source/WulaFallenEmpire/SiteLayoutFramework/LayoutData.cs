using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public enum QuestTagPrefixMode
    {
        None,
        FromSiteQuestTag
    }

    public class LayoutRoofData
    {
        public RoofDef roof;
        public List<CellRect> rects = new List<CellRect>();
    }

    public class LayoutPawnSpawnEntry
    {
        public IntVec3 position;
        public int spawnRadius = 4;
        public List<LayoutPawnSpawnGroup> groups = new List<LayoutPawnSpawnGroup>();
    }

    public class LayoutPawnSpawnGroup
    {
        public List<LayoutPawnSpawnOption> options = new List<LayoutPawnSpawnOption>();
    }

    public class LayoutPawnSpawnOption
    {
        public PawnKindDef kind;
        public FactionDef faction;
        public IntRange count = IntRange.One;
        public string lordGroup;
        public string lordJob = "DefendPoint";
        public string questTag;
        public List<ThingDefCountClass> inventory;
    }

    public class LayoutContainerContentData
    {
        public ThingDef thingDef;
        public List<ThingDefCountClass> contents = new List<ThingDefCountClass>();
        public bool fillAllMatching = true;
        public IntVec3 position = IntVec3.Invalid;
        public int radius = -1;
    }
}
