using System;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_ChangeExpression : AITool
    {
        public override string Name => "change_expression";
        public override string Description => "Changes your visual expression/portrait to match your current mood or reaction.";
        public override string UsageSchema => "<change_expression><expression_id>int (1-6)</expression_id></change_expression>";

        public override string Execute(string args)
        {
            try
            {
                var parsedArgs = ParseXmlArgs(args);
                int id = 0;
                
                if (parsedArgs.TryGetValue("expression_id", out string idStr))
                {
                    if (int.TryParse(idStr, out id))
                    {
                        var core = AIIntelligenceCore.Instance;
                        if (core != null)
                        {
                            core.SetPortrait(id);
                            return $"Expression changed to {id}.";
                        }
                        return "Error: AI Core not found.";
                    }
                    return "Error: Invalid arguments. 'expression_id' must be an integer.";
                }
                
                return "Error: Missing <expression_id> parameter.";
            }
            catch (Exception ex)
            {
                return $"Error executing tool: {ex.Message}";
            }
        }
    }
}
