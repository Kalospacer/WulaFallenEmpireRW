using System;
using Verse;
using WulaFallenEmpire.EventSystem.AI.UI;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_ChangeExpression : AITool
    {
        public override string Name => "change_expression";
        public override string Description => "Changes your visual expression/portrait to match your current mood or reaction.";
        public override string UsageSchema => "{\"expression_id\": \"int (1-6)\"}";

        public override string Execute(string args)
        {
            try
            {
                var json = SimpleJsonParser.Parse(args);
                int id = 0;
                if (json.TryGetValue("expression_id", out string idStr) && int.TryParse(idStr, out id))
                {
                    var window = Find.WindowStack.WindowOfType<Dialog_AIConversation>();
                    if (window != null)
                    {
                        window.SetPortrait(id);
                        return $"Expression changed to {id}.";
                    }
                    return "Error: Dialog window not found.";
                }
                return "Error: Invalid arguments. 'expression_id' must be an integer.";
            }
            catch (Exception ex)
            {
                return $"Error executing tool: {ex.Message}";
            }
        }
    }
}