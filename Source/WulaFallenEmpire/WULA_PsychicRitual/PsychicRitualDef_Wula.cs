using RimWorld;

namespace WulaFallenEmpire
{
    /// <summary>
    /// This class serves as a custom base for all Wula rituals.
    /// It inherits from PsychicRitualDef_InvocationCircle to retain all vanilla functionality,
    /// but provides a unique type that our custom CompWulaRitualSpot can specifically look for,
    /// ensuring these rituals only appear on our custom ritual spot.
    /// </summary>
    public class PsychicRitualDef_Wula : PsychicRitualDef_WulaBase
    {
        // This class can be expanded with Wula-specific ritual properties if needed in the future.
        // For now, its existence is enough to separate our rituals from the vanilla ones.
    }
}