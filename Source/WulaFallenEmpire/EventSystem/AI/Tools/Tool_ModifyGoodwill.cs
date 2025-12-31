using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_ModifyGoodwill : AITool
    {
        public override string Name => "modify_goodwill";
        public override string Description => "Adjusts YOUR internal opinion of the player (AI Goodwill). WARNING: This DOES NOT affect Faction Relations or stop raids. It is purely personal. Do NOT use this to try to stop enemies.";
        public override string UsageSchema => "{\"amount\": 1}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["amount"] = SchemaInteger("Change in goodwill (-5 to 5).", nullable: true)
            };
            return SchemaObject(properties, RequiredList("amount"));
        }

        public override string Execute(string args)
        {
            try
            {
                var parsedArgs = ParseJsonArgs(args);
                int amount = 0;

                if (!TryGetInt(parsedArgs, "amount", out amount))
                {
                    // Fallback for simple number string
                    if (!int.TryParse(args?.Trim(), out amount))
                    {
                        return "Error: Missing 'amount' parameter.";
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
