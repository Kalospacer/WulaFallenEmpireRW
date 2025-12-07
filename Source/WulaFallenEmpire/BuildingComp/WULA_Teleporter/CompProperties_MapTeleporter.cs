using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MapTeleporter : CompProperties
    {
        public float radius = 5f;
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