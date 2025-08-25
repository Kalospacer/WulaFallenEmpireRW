using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class PocketMapProperties : DefModExtension
    {
        public IntVec2 pocketMapSize = new IntVec2(13, 13);
        public MapGeneratorDef mapGenerator;
        public ThingDef exitDef;
    }
}