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

        /// <summary>
        /// Normalizes a tool's own schema into the shape providers accept.
        /// </summary>
        /// <remarks>
        /// Each tool's <c>GetParametersSchema</c> is the single source of truth for its parameters and
        /// required fields. This only sanitizes and fills in structural defaults; it must not rewrite
        /// what the tool declared.
        /// </remarks>
        /// <param name="tool">Tool whose schema should be resolved.</param>
        /// <returns>A provider-ready JSON schema object.</returns>
        public JObject GetCanonicalSchema(AITool tool)
        {
            var raw = tool.GetParametersSchema() ?? new Dictionary<string, object>();
            var sanitized = ToolSchemaSanitizer.Sanitize(raw);
            var schema = JObject.FromObject(sanitized);
            RemoveUnsupportedNullTypes(schema);
            if (schema["type"] == null) schema["type"] = "object";
            if (schema["properties"] == null) schema["properties"] = new JObject();
            if (schema["required"] == null) schema["required"] = new JArray();
            if (schema["additionalProperties"] == null) schema["additionalProperties"] = false;
            return schema;
        }

        /// <summary>
        /// Full surface for player-driven conversation: the read-only tools plus everything that
        /// changes game or memory state.
        /// </summary>
        /// <param name="enableVisionTools">Whether the VLM screen tool should be offered.</param>
        /// <returns>A registry containing every tool.</returns>
        public static AIToolRegistry CreateDefault(bool enableVisionTools)
        {
            var registry = CreateObserver(enableVisionTools);
            registry.Add(new Tool_SpawnResources());
            registry.Add(new Tool_ModifyGoodwill());
            registry.Add(new Tool_SendReinforcement());
            registry.Add(new Tool_CallBombardment());
            registry.Add(new Tool_CallPrefabAirdrop());
            registry.Add(new Tool_SetOverwatchMode());
            registry.Add(new Tool_RememberFact());
            registry.Add(new Tool_ReadSkill());
            registry.Add(new Tool_McpFindTools());
            registry.Add(new Tool_McpToolDetail());
            registry.Add(new Tool_McpInvoke());
            registry.Add(new Tool_BridgeListTools());
            registry.Add(new Tool_BridgeCall());
            return registry;
        }

        /// <summary>
        /// Read-only surface for turns the player did not initiate, such as automatic letter commentary.
        /// </summary>
        /// <remarks>
        /// The commentary path is triggered by a Harmony postfix on every incoming letter, so any tool
        /// offered here can fire with no player input. Nothing that spawns things, bombards the map,
        /// shifts goodwill, or writes durable memory belongs on that path; expression changes are kept
        /// because the commentary flow drives the portrait.
        /// </remarks>
        /// <param name="enableVisionTools">Whether the VLM screen tool should be offered.</param>
        /// <returns>A registry containing only observation tools.</returns>
        public static AIToolRegistry CreateObserver(bool enableVisionTools)
        {
            var registry = new AIToolRegistry();
            registry.Add(new Tool_GetPawnStatus());
            registry.Add(new Tool_GetMapResources());
            registry.Add(new Tool_GetAvailablePrefabs());
            registry.Add(new Tool_GetMapPawns());
            registry.Add(new Tool_GetAvailableBombardments());
            registry.Add(new Tool_SearchThingDef());
            registry.Add(new Tool_SearchPawnKind());
            registry.Add(new Tool_GetRecentNotifications());
            registry.Add(new Tool_RecallMemories());
            registry.Add(new Tool_ChangeExpression());
            if (enableVisionTools)
            {
                registry.Add(new Tool_AnalyzeScreen());
            }
            registry.Add(new Tool_McpFindTools());
            registry.Add(new Tool_McpToolDetail());
            registry.Add(new Tool_BridgeListTools());
            return registry;
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
