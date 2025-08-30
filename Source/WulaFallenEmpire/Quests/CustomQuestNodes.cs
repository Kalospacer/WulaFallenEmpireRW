using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace WulaFallenEmpire.Quests
{
    public class QuestNode_DropShuttleForRecovery : QuestNode
    {
        [NoTranslate]
        public SlateRef<Map> map;
        
        [NoTranslate]
        public SlateRef<Thing> itemToRecover;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Map targetMap = map.GetValue(slate);
            Thing item = itemToRecover.GetValue(slate);

            if (targetMap == null || item == null) return;

            var shuttle = ThingMaker.MakeThing(ThingDefOf.Shuttle) as ThingWithComps;
            if (shuttle == null) return;
            
            var comp = shuttle.TryGetComp<CompTransporter>();
            if (comp != null)
            {
                comp.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
                var questComponent = Current.Game.GetComponent<GameComponent_WulaQuests>();
                questComponent?.RegisterRecoveryQuest(item, comp.groupID);
            }

            DropCellFinder.TryFindDropSpotNear(targetMap.Center, targetMap, out var spot, allowFogged: false, canRoofPunch: false);
            DropPodUtility.DropThingsNear(spot, targetMap, new[] { shuttle });
        }

        protected override bool TestRunInt(Slate slate)
        {
            return map.GetValue(slate) != null && itemToRecover.GetValue(slate) != null;
        }
    }

    public class QuestNode_AddThingRules : QuestNode
    {
        [NoTranslate]
        public SlateRef<Thing> thing;

        [NoTranslate]
        public SlateRef<string> prefix;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Thing value = thing.GetValue(slate);
            if (value != null)
            {
                var rulePack = new RulePack();
                rulePack.Rules.Add(new Rule_String(prefix.GetValue(slate) + "_label", value.Label));
                QuestGen.AddQuestDescriptionRules(rulePack);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return thing.GetValue(slate) != null;
        }
    }

    public class QuestNode_SpawnThing_Wula : QuestNode
    {
        [NoTranslate]
        public SlateRef<MapParent> mapParent;
        [NoTranslate]
        public SlateRef<Thing> thing;
        [NoTranslate]
        public SlateRef<Faction> faction;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            var part = new QuestPart_SpawnThing();
            part.mapParent = mapParent.GetValue(slate);
            part.thing = thing.GetValue(slate);
            part.factionForFindingSpot = faction.GetValue(slate);
            part.inSignal = QuestGen.slate.Get<string>("inSignal");
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return mapParent.GetValue(slate) != null && thing.GetValue(slate) != null;
        }
    }
}