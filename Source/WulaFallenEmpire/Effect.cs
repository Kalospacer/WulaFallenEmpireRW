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
}
