using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MapTeleporter : CompProperties
    {
        public IntVec2 areaSize = new IntVec2(13, 13);
        public int warmupTicks = 120;
        public EffecterDef warmupEffecter;
        public SoundDef warmupSound;
        public SoundDef teleportSound;
        
        public ResearchProjectDef requiredResearch;
        
        public CompProperties_MapTeleporter()
        {
            compClass = typeof(CompMapTeleporter);
        }
    }
}