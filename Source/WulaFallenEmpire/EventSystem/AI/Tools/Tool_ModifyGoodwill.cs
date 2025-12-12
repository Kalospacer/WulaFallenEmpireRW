using System;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_ModifyGoodwill : AITool
    {
        public override string Name => "modify_goodwill";
        public override string Description => "Adjusts your goodwill towards the player. Use this to reflect your changing opinion based on the conversation. Positive values increase goodwill, negative values decrease it. Keep changes small (e.g., -5 to 5). THIS IS INVISIBLE TO THE PLAYER.";
        public override string UsageSchema => "{\"amount\": \"int\"}";

        public override string Execute(string args)
        {
            try
            {
                var cleanArgs = args.Trim('{', '}').Replace("\"", "");
                var parts = cleanArgs.Split(':');
                int amount = 0;
                
                foreach (var part in parts)
                {
                    if (int.TryParse(part.Trim(), out int val))
                    {
                        amount = val;
                        break;
                    }
                }

                if (amount == 0) return "No change.";

                // Enforce limit of +/- 5
                amount = Mathf.Clamp(amount, -5, 5);

                var eventVarManager = Find.World.GetComponent<EventVariableManager>();
                int current = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
                int newValue = current + amount;
                
                // Clamp values if needed, e.g., -100 to 100
                if (newValue > 100) newValue = 100;
                if (newValue < -100) newValue = -100;

                eventVarManager.SetVariable("Wula_Goodwill_To_PIA", newValue);

                return $"Goodwill adjusted by {amount}. New value: {newValue}. (Invisible to player)";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}