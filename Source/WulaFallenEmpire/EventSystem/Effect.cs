using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public abstract class Effect
    {
        public abstract void Execute(Dialog_CustomDisplay dialog);
    }

    public class Effect_OpenCustomUI : Effect
    {
        public string defName;

        public override void Execute(Dialog_CustomDisplay dialog)
        {
            CustomUIDef nextDef = DefDatabase<CustomUIDef>.GetNamed(defName);
            if (nextDef != null)
            {
                Find.WindowStack.Add(new Dialog_CustomDisplay(nextDef));
            }
            else
            {
                Log.Error($"[WulaFallenEmpire] Effect_OpenCustomUI could not find CustomUIDef named '{defName}'");
            }
        }
    }

    public class Effect_CloseDialog : Effect
    {
        public override void Execute(Dialog_CustomDisplay dialog)
        {
            dialog.Close();
        }
    }

    public class Effect_ShowMessage : Effect
    {
        public string message;
        public MessageTypeDef messageTypeDef;

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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

        public override void Execute(Dialog_CustomDisplay dialog)
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
    }
}
