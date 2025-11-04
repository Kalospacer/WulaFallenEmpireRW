using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_Launchable_ToGlobalStorage : CompProperties_Launchable_TransportPod
    {
        public float fuelNeededToLaunch = 25f;
        public SoundDef launchSound;

        public CompProperties_Launchable_ToGlobalStorage()
        {
            this.compClass = typeof(CompLaunchable_ToGlobalStorage);
        }
    }
}