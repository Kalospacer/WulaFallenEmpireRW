using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class VerbProperties_Excalibur : VerbProperties
    {
        public float pathWidth = 1f; // Default path width
        public DamageDef damageDef; // Custom damage type
        public float damageAmount = -1f; // Custom damage amount
        public float armorPenetration = -1f; // Custom armor penetration
        public float maxRange = 1000f; // Default max range for beams
    }
}