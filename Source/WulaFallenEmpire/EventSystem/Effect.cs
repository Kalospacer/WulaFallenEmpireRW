using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public abstract class Effect
    {
        // Allow the dialog to be null for contexts where there isn't one (like quests)
        public abstract void Execute(Dialog_CustomDisplay dialog = null);
    }

    public class Effect_OpenCustomUI : Effect
    {
        public string defName;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            EventDef nextDef = DefDatabase<EventDef>.GetNamed(defName);
            if (nextDef != null)
            {
                if (nextDef.hiddenWindow)
                {
                    // Since effects are merged in PostLoad, we only need to execute dismissEffects here.
                    if (!nextDef.dismissEffects.NullOrEmpty())
                    {
                        foreach (var conditionalEffect in nextDef.dismissEffects)
                        {
                            string reason;
                            if (AreConditionsMet(conditionalEffect.conditions, out reason))
                            {
                                if (!conditionalEffect.effects.NullOrEmpty())
                                {
                                    foreach (var effect in conditionalEffect.effects)
                                    {
                                        effect.Execute(null);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Find.WindowStack.Add(new Dialog_CustomDisplay(nextDef));
                }
            }
            else
            {
                Log.Error($"[WulaFallenEmpire] Effect_OpenCustomUI could not find EventDef named '{defName}'");
            }
        }

        private bool AreConditionsMet(List<Condition> conditions, out string reason)
        {
            reason = "";
            if (conditions.NullOrEmpty())
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(out string singleReason))
                {
                    reason = singleReason;
                    return false;
                }
            }
            return true;
        }
    }

    public class Effect_CloseDialog : Effect
    {
        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            // Only close the dialog if it exists
            dialog?.Close();
        }
    }

    public class Effect_ShowMessage : Effect
    {
        public string message;
        public MessageTypeDef messageTypeDef;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (messageTypeDef == null)
            {
                messageTypeDef = MessageTypeDefOf.PositiveEvent;
            }
            Messages.Message(message, messageTypeDef);
        }
    }

    public class Effect_FireIncident : Effect
    {
        public IncidentDef incident;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (incident == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_FireIncident has a null incident Def.");
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = Find.CurrentMap,
                forced = true
            };

            if (!incident.Worker.TryExecute(parms))
            {
                Log.Error($"[WulaFallenEmpire] Could not fire incident {incident.defName}");
            }
        }
    }

    public class Effect_ChangeFactionRelation : Effect
    {
        public FactionDef faction;
        public int goodwillChange;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (faction == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_ChangeFactionRelation has a null faction Def.");
                return;
            }

            Faction targetFaction = Find.FactionManager.FirstFactionOfDef(faction);
            if (targetFaction == null)
            {
                Log.Warning($"[WulaFallenEmpire] Could not find an active faction for FactionDef '{faction.defName}'.");
                return;
            }

            Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, goodwillChange, canSendMessage: true, canSendHostilityLetter: true, reason: null, lookTarget: null);
        }
    }

    public class Effect_SetVariable : Effect
    {
        public string name;
        public string value;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            // Try to parse as int, then float, otherwise keep as string
            if (int.TryParse(value, out int intValue))
            {
                EventContext.SetVariable(name, intValue);
            }
            else if (float.TryParse(value, out float floatValue))
            {
                EventContext.SetVariable(name, floatValue);
            }
            else
            {
                EventContext.SetVariable(name, value);
            }
        }
    }
    
    public class Effect_ChangeFactionRelation_FromVariable : Effect
    {
        public FactionDef faction;
        public string goodwillVariableName;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (faction == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_ChangeFactionRelation_FromVariable has a null faction Def.");
                return;
            }
            
            Faction targetFaction = Find.FactionManager.FirstFactionOfDef(faction);
            if (targetFaction == null)
            {
                Log.Warning($"[WulaFallenEmpire] Could not find an active faction for FactionDef '{faction.defName}'.");
                return;
            }

            int goodwillChange = EventContext.GetVariable<int>(goodwillVariableName);
            Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, goodwillChange, canSendMessage: true, canSendHostilityLetter: true, reason: null, lookTarget: null);
        }
    }

    public class Effect_SpawnPawnAndStore : Effect
    {
        public PawnKindDef kindDef;
        public int count = 1;
        public string storeAs;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (kindDef == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_SpawnPawnAndStore has a null kindDef.");
                return;
            }
            if (storeAs.NullOrEmpty())
            {
                Log.Error("[WulaFallenEmpire] Effect_SpawnPawnAndStore needs a 'storeAs' variable name.");
                return;
            }

            List<Pawn> spawnedPawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                Pawn newPawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfPlayer);
                IntVec3 loc = CellFinder.RandomSpawnCellForPawnNear(Find.CurrentMap.mapPawns.FreeColonists.First().Position, Find.CurrentMap, 10);
                GenSpawn.Spawn(newPawn, loc, Find.CurrentMap);
                spawnedPawns.Add(newPawn);
            }

            if (count == 1)
            {
                EventContext.SetVariable(storeAs, spawnedPawns.First());
            }
            else
            {
                EventContext.SetVariable(storeAs, spawnedPawns);
            }
        }
    }

    public class Effect_GiveThing : Effect
    {
        public ThingDef thingDef;
        public int count = 1;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (thingDef == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_GiveThing has a null thingDef.");
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_GiveThing cannot execute without a current map.");
                return;
            }

            Thing thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = count;

            IntVec3 dropCenter = DropCellFinder.TradeDropSpot(currentMap);
            DropPodUtility.DropThingsNear(dropCenter, currentMap, new List<Thing> { thing }, 110, false, false, false, false);

            Messages.Message("LetterLabelCargoPodCrash".Translate(), new TargetInfo(dropCenter, currentMap), MessageTypeDefOf.PositiveEvent);
        }
    }

    public class Effect_SpawnPawn : Effect
    {
        public PawnKindDef kindDef;
        public int count = 1;
        public bool joinPlayerFaction = true;
        public string letterLabel;
        public string letterText;
        public LetterDef letterDef;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (kindDef == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_SpawnPawn has a null kindDef.");
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_SpawnPawn cannot execute without a current map.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Faction faction = joinPlayerFaction ? Faction.OfPlayer : null;
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef, faction, PawnGenerationContext.NonPlayer, -1, true, false, false, false, 
                    true, 20f, false, true, false, true, true, false, false, false, false, 0f, 0f, null, 1f, 
                    null, null, null, null, null, null, null, null, null, null, null, null, false
                );
                Pawn pawn = PawnGenerator.GeneratePawn(request);

                if (!CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => map.reachability.CanReachColony(c) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Neutral, out IntVec3 cell))
                {
                    cell = DropCellFinder.RandomDropSpot(map);
                }
                
                GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);

                if (!string.IsNullOrEmpty(letterLabel) && !string.IsNullOrEmpty(letterText))
                {
                    TaggedString finalLabel = letterLabel.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn);
                    TaggedString finalText = letterText.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn);
                    PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref finalText, ref finalLabel, pawn);
                    Find.LetterStack.ReceiveLetter(finalLabel, finalText, letterDef ?? LetterDefOf.PositiveEvent, pawn);
            }
        }
    }

    public enum VariableOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    public class Effect_ModifyVariable : Effect
    {
        public string name;
        public float value;
        public VariableOperation operation;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Error("[WulaFallenEmpire] Effect_ModifyVariable has a null or empty name.");
                return;
            }

            if (!EventContext.HasVariable(name))
            {
                EventContext.SetVariable(name, 0f);
            }
            
            float currentValue = EventContext.GetVariable<float>(name);

            switch (operation)
            {
                case VariableOperation.Add:
                    currentValue += value;
                    break;
                case VariableOperation.Subtract:
                    currentValue -= value;
                    break;
                case VariableOperation.Multiply:
                    currentValue *= value;
                    break;
                case VariableOperation.Divide:
                    if (value != 0)
                    {
                        currentValue /= value;
                    }
                    else
                    {
                        Log.Error($"[WulaFallenEmpire] Effect_ModifyVariable tried to divide by zero for variable '{name}'.");
                    }
                    break;
            }

            EventContext.SetVariable(name, currentValue);
        }
    }

    public class Effect_ClearVariable : Effect
    {
        public string name;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Error("[WulaFallenEmpire] Effect_ClearVariable has a null or empty name.");
                return;
            }
            EventContext.ClearVariable(name);
        }
    }

    public class Effect_AddQuest : Effect
    {
        public QuestScriptDef quest;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (quest == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_AddQuest has a null quest Def.");
                return;
            }

            Quest newQuest = Quest.MakeRaw();
            newQuest.root = quest;
            newQuest.id = Find.UniqueIDsManager.GetNextQuestID();
            Find.QuestManager.Add(newQuest);
        }
    }

    public class Effect_FinishResearch : Effect
    {
        public ResearchProjectDef research;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            if (research == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_FinishResearch has a null research Def.");
                return;
            }

            Find.ResearchManager.FinishProject(research);
        }
    }
}
public class Effect_TriggerRaid : Effect
    {
        public float points;
        public FactionDef faction;
        public RaidStrategyDef raidStrategy;
        public PawnsArrivalModeDef raidArrivalMode;
        public PawnGroupKindDef groupKind;
        public List<PawnGroupMaker> pawnGroupMakers;
        public string letterLabel;
        public string letterText;

        public override void Execute(Dialog_CustomDisplay dialog = null)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_TriggerRaid cannot execute without a current map.");
                return;
            }

            Faction factionInst = Find.FactionManager.FirstFactionOfDef(this.faction);
            if (factionInst == null)
            {
                Log.Error($"[WulaFallenEmpire] Effect_TriggerRaid could not find an active faction for FactionDef '{this.faction?.defName}'.");
                return;
            }

            // If custom pawn groups are defined, use them.
            if (!pawnGroupMakers.NullOrEmpty())
            {
                IncidentParms parms = new IncidentParms
                {
                    target = map,
                    points = this.points,
                    faction = factionInst,
                    raidStrategy = this.raidStrategy,
                    raidArrivalMode = this.raidArrivalMode,
                    pawnGroupMakerSeed = Rand.Int,
                    forced = true
                };

                if (!RCellFinder.TryFindRandomPawnEntryCell(out parms.spawnCenter, map, CellFinder.EdgeRoadChance_Hostile))
                {
                    Log.Error("[WulaFallenEmpire] Effect_TriggerRaid could not find a valid spawn center.");
                    return;
                }

                PawnGroupMakerParms groupMakerParms = new PawnGroupMakerParms
                {
                    groupKind = this.groupKind ?? PawnGroupKindDefOf.Combat,
                    tile = map.Tile,
                    points = this.points,
                    faction = factionInst,
                    seed = parms.pawnGroupMakerSeed
                };

                if (!pawnGroupMakers.TryRandomElement(out var chosenGroupMaker))
                {
                    Log.Error($"[WulaFallenEmpire] Effect_TriggerRaid could not find a suitable PawnGroupMaker for {points} points with groupKind {groupMakerParms.groupKind.defName} from the provided list.");
                    return;
                }

                List<Pawn> pawns = chosenGroupMaker.GeneratePawns(groupMakerParms).ToList();
                if (!pawns.Any())
                {
                    Log.Error("[WulaFallenEmpire] Effect_TriggerRaid generated no pawns with the custom pawnGroupMakers.");
                    return;
                }

                parms.raidArrivalMode.Worker.Arrive(pawns, parms);

                parms.raidStrategy.Worker.MakeLords(parms, pawns);
                
                TaggedString finalLabel;
                if (!string.IsNullOrEmpty(this.letterLabel))
                {
                    finalLabel = this.letterLabel;
                }
                else
                {
                    finalLabel = "LetterLabelRaid".Translate(factionInst.def.label).CapitalizeFirst();
                }

                TaggedString finalText;
                if (!string.IsNullOrEmpty(this.letterText))
                {
                    finalText = this.letterText;
                }
                else
                {
                    finalText = "LetterRaid".Translate(
                        factionInst.Name,
                        factionInst.def.pawnsPlural,
                        parms.raidStrategy.arrivalTextEnemy
                    ).CapitalizeFirst();
                }
                
                Pawn mostImportantPawn = pawns.FirstOrDefault();
                TargetInfo target = mostImportantPawn != null ? new TargetInfo(mostImportantPawn) : new TargetInfo(parms.spawnCenter, map);

                Find.LetterStack.ReceiveLetter(finalLabel, finalText, LetterDefOf.ThreatBig, target, factionInst);
            }
            else // Fallback to default raid incident worker
            {
                IncidentParms parms = new IncidentParms
                {
                    target = map,
                    points = this.points,
                    faction = factionInst,
                    raidStrategy = this.raidStrategy,
                    raidArrivalMode = this.raidArrivalMode,
                    forced = true
                };
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
        }
    }
}
