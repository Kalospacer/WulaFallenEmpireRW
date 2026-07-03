using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace WulaFallenEmpire
{
    public class QuestNode_GetFactionOfDef : QuestNode
    {
        private const string LogPrefix = "[SiteLayoutFramework]";

        public SlateRef<FactionDef> factionDef;

        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate)
        {
            return TrySetFaction(slate);
        }

        protected override void RunInt()
        {
            TrySetFaction(QuestGen.slate);
        }

        private bool TrySetFaction(Slate slate)
        {
            FactionDef def = factionDef.GetValue(slate);
            if (def == null)
            {
                Log.Warning($"{LogPrefix} QuestNode_GetFactionOfDef: factionDef is null.");
                return false;
            }

            Faction faction = Find.FactionManager.FirstFactionOfDef(def);
            if (faction == null)
            {
                Log.Warning($"{LogPrefix} QuestNode_GetFactionOfDef: no faction of def {def.defName} in world.");
                return false;
            }

            slate.Set(storeAs.GetValue(slate), faction);
            return true;
        }
    }
}
