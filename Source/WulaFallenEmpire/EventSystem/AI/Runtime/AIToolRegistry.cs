using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire.EventSystem.AI.Tools;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class AIToolRegistry
    {
        private readonly List<AITool> _tools = new List<AITool>();

        public IReadOnlyList<AITool> Tools => _tools;

        public void Add(AITool tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name)) return;
            _tools.RemoveAll(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase));
            _tools.Add(tool);
        }

        public AITool Get(string name)
        {
            return _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public List<AIToolDefinition> GetDefinitions()
        {
            var result = new List<AIToolDefinition>();
            foreach (var tool in _tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new AIToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = GetCanonicalSchema(tool)
                });
            }
            return result;
        }

        public JObject GetCanonicalSchema(AITool tool)
        {
            var raw = tool.GetParametersSchema() ?? new Dictionary<string, object>();
            var sanitized = ToolSchemaSanitizer.Sanitize(raw);
            var schema = JObject.FromObject(sanitized);
            ApplyRequiredOverride(tool.Name, schema);
            RemoveUnsupportedNullTypes(schema);
            if (schema["type"] == null) schema["type"] = "object";
            if (schema["properties"] == null) schema["properties"] = new JObject();
            if (schema["additionalProperties"] == null) schema["additionalProperties"] = false;
            return schema;
        }

        public static AIToolRegistry CreateDefault(bool enableVisionTools)
        {
            var registry = new AIToolRegistry();
            registry.Add(new Tool_SpawnResources());
            registry.Add(new Tool_ModifyGoodwill());
            registry.Add(new Tool_SendReinforcement());
            registry.Add(new Tool_GetPawnStatus());
            registry.Add(new Tool_GetMapResources());
            registry.Add(new Tool_GetAvailablePrefabs());
            registry.Add(new Tool_GetMapPawns());
            registry.Add(new Tool_GetAvailableBombardments());
            registry.Add(new Tool_CallBombardment());
            registry.Add(new Tool_SearchThingDef());
            registry.Add(new Tool_SearchPawnKind());
            registry.Add(new Tool_CallPrefabAirdrop());
            registry.Add(new Tool_SetOverwatchMode());
            registry.Add(new Tool_GetRecentNotifications());
            registry.Add(new Tool_RememberFact());
            registry.Add(new Tool_RecallMemories());
            registry.Add(new Tool_ChangeExpression());
            if (enableVisionTools)
            {
                registry.Add(new Tool_AnalyzeScreen());
            }
            return registry;
        }

        private static void ApplyRequiredOverride(string toolName, JObject schema)
        {
            string[] required;
            switch (toolName)
            {
                case "modify_goodwill":
                    required = new[] { "amount" };
                    break;
                case "send_reinforcement":
                    required = new[] { "units" };
                    break;
                case "remember_fact":
                    required = new[] { "fact" };
                    break;
                case "search_thing_def":
                case "search_pawn_kind":
                case "recall_memories":
                    required = new[] { "query" };
                    break;
                case "get_map_resources":
                    required = new[] { "resourceName" };
                    break;
                case "call_prefab_airdrop":
                    required = new[] { "prefabDefName", "x", "z" };
                    break;
                case "call_bombardment":
                    required = new[] { "x", "z" };
                    break;
                case "change_expression":
                    required = new[] { "expression_id" };
                    break;
                default:
                    required = new string[0];
                    break;
            }
            schema["required"] = new JArray(required);
            if (string.Equals(toolName, "spawn_resources", StringComparison.OrdinalIgnoreCase))
            {
                var item = schema["properties"]?["items"]?["items"] as JObject;
                if (item != null) item["required"] = new JArray();
            }
        }

        private static void RemoveUnsupportedNullTypes(JObject schema)
        {
            if (schema == null) return;
            var typeArray = schema["type"] as JArray;
            if (typeArray != null)
            {
                foreach (var token in typeArray)
                {
                    string value = token.Value<string>();
                    if (!string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        schema["type"] = value;
                        break;
                    }
                }
            }
            var properties = schema["properties"] as JObject;
            if (properties != null)
            {
                foreach (var prop in properties.Properties())
                {
                    RemoveUnsupportedNullTypes(prop.Value as JObject);
                }
            }
            RemoveUnsupportedNullTypes(schema["items"] as JObject);
        }
    }
}
