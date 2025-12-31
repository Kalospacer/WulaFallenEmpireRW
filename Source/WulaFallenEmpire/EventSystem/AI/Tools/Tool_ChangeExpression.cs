using System;
using System.Collections.Generic;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_ChangeExpression : AITool
    {
        public override string Name => "change_expression";
        public override string Description => "Changes your visual expression/portrait to match your current mood or reaction.";
        public override string UsageSchema => "{\"expression_id\": 2}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["expression_id"] = SchemaInteger("Expression id (1-6).", nullable: true)
            };
            return SchemaObject(properties, RequiredList("expression_id"));
        }

        public override string Execute(string args)
        {
            try
            {
                var parsedArgs = ParseJsonArgs(args);
                int id = 0;
                
                if (TryGetInt(parsedArgs, "expression_id", out id))
                {
                    var core = AIIntelligenceCore.Instance;
                    if (core != null)
                    {
                        core.SetPortrait(id);
                        return $"Expression changed to {id}.";
                    }
                    return "Error: AI Core not found.";
                }
                
                return "Error: Missing 'expression_id' parameter.";
            }
            catch (Exception ex)
            {
                return $"Error executing tool: {ex.Message}";
            }
        }
    }
}
