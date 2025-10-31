// JobExtension_AutonomousCombat.cs
using Verse;

namespace WulaFallenEmpire
{
    public class JobExtension_AutonomousCombat : DefModExtension
    {
        public bool canUseRangedWeapon = true;
        public bool autoAttackEnabled = true;
        public int attackSearchRadius = 25;
        public bool ignoreWorkTags = true;
        public bool forceFireAtWill = true;
    }
}
