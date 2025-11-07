using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompExperienceDataPack : ThingComp
    {
        public float storedExperience;
        public string sourceWeaponLabel;

        public CompProperties_ExperienceDataPack Props => (CompProperties_ExperienceDataPack)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedExperience, "storedExperience", 0f);
            Scribe_Values.Look(ref sourceWeaponLabel, "sourceWeaponLabel");
        }

        public override string CompInspectStringExtra()
        {
            return "WULA_DataPackExperience".Translate(storedExperience.ToString("F0"), sourceWeaponLabel ?? "Unknown");
        }
    }

    public class CompProperties_ExperienceDataPack : CompProperties
    {
        public CompProperties_ExperienceDataPack()
        {
            compClass = typeof(CompExperienceDataPack);
        }
    }
}
