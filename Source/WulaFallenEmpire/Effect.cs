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

            Faction.OfPlayer.TryAffectGoodwillWith(faction, goodwillChange, canSendMessage: true, canSendHostilityLetter: true, reason: HistoryEventDefOf.QuestGoodwill, lookTarget: null);
