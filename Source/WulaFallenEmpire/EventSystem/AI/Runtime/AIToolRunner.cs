using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire;
using WulaFallenEmpire.EventSystem.AI.Tools;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class AIToolRunner
    {
        private readonly AIToolRegistry _registry;

        public AIToolRunner(AIToolRegistry registry)
        {
            _registry = registry;
        }

        public async Task<AIToolResult> ExecuteAsync(AIToolCall call, CancellationToken cancellationToken)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Name))
            {
                return Error(call, "Error: Empty tool call.");
            }
            var tool = _registry.Get(call.Name);
            if (tool == null)
            {
                return Error(call, $"Error: Tool '{call.Name}' not found.");
            }

            var schema = _registry.GetCanonicalSchema(tool);
            var filteredArgs = FilterArguments(call.Arguments ?? new JObject(), schema);
            string validationError;
            if (!ValidateArguments(filteredArgs, schema, out validationError))
            {
                return Error(call, $"Error: Tool '{call.Name}' arguments failed validation. {validationError}");
            }

            string argsJson = filteredArgs.ToString(Newtonsoft.Json.Formatting.None);
            string label = BuildLabel(call);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogTool(label, "queued", argsJson, 0);
                string result = await AIMainThreadDispatcher.InvokeAsync(
                    async () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        LogTool(label, "started", null, stopwatch.ElapsedMilliseconds);
                        return await tool.ExecuteAsync(argsJson, cancellationToken);
                    },
                    cancellationToken,
                    label);
                result = result?.Trim() ?? string.Empty;
                LogTool(label, "completed", TrimForLog(result), stopwatch.ElapsedMilliseconds);
                return new AIToolResult
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = result,
                    IsError = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                };
            }
            catch (OperationCanceledException)
            {
                LogTool(label, "cancelled", null, stopwatch.ElapsedMilliseconds);
                return Error(call, $"Error: Tool '{call.Name}' cancelled or timed out.");
            }
            catch (Exception ex)
            {
                LogTool(label, "failed: " + ex.Message, null, stopwatch.ElapsedMilliseconds);
                return Error(call, $"Error: {ex.Message}");
            }
        }

        private static string BuildLabel(AIToolCall call)
        {
            string name = string.IsNullOrWhiteSpace(call?.Name) ? "unknown_tool" : call.Name;
            string id = string.IsNullOrWhiteSpace(call?.Id) ? "no_call_id" : call.Id;
            return $"tool:{name}:{id}";
        }

        private static void LogTool(string label, string stage, string detail, long elapsedMs)
        {
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " detail=" + detail;
            WulaLog.Debug($"[WulaAI][Tool][{label}] {stage}, elapsedMs={elapsedMs}{suffix}");
        }

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= 1200 ? value : value.Substring(0, 1200) + $"...[truncated {value.Length - 1200} chars]";
        }

        private static AIToolResult Error(AIToolCall call, string content)
        {
            return new AIToolResult
            {
                ToolCallId = call?.Id ?? string.Empty,
                ToolName = call?.Name ?? string.Empty,
                Content = content ?? "Error: Unknown tool error.",
                IsError = true
            };
        }

        private static JObject FilterArguments(JObject args, JObject schema)
        {
            var result = new JObject();
            var properties = schema["properties"] as JObject;
            if (properties == null) return result;
            foreach (var arg in args.Properties())
            {
                string canonical = FindPropertyName(properties, arg.Name);
                if (canonical == null) continue;
                result[canonical] = arg.Value.DeepClone();
            }
            return result;
        }

        private static string FindPropertyName(JObject properties, string name)
        {
            foreach (var prop in properties.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Name;
                }
            }
            return null;
        }

        private static bool ValidateArguments(JObject args, JObject schema, out string error)
        {
            error = null;
            var properties = schema["properties"] as JObject;
            var required = schema["required"] as JArray;
            if (required != null)
            {
                foreach (var item in required)
                {
                    string name = item.Value<string>();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (args[name] == null || args[name].Type == JTokenType.Null)
                    {
                        error = $"Missing required field '{name}'.";
                        return false;
                    }
                }
            }
            if (properties == null) return true;
            foreach (var prop in args.Properties())
            {
                var propSchema = properties[prop.Name] as JObject;
                if (propSchema == null) continue;
                if (!ValidateValue(prop.Value, propSchema, out error))
                {
                    error = $"Field '{prop.Name}' is invalid. {error}";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateValue(JToken value, JObject schema, out string error)
        {
            error = null;
            string type = schema.Value<string>("type");
            if (string.IsNullOrWhiteSpace(type)) return true;
            if (value == null || value.Type == JTokenType.Null)
            {
                error = "Value must not be null.";
                return false;
            }
            switch (type)
            {
                case "string":
                    if (value.Type == JTokenType.String) return true;
                    error = "Expected string.";
                    return false;
                case "boolean":
                    if (value.Type == JTokenType.Boolean) return true;
                    if (value.Type == JTokenType.String && bool.TryParse(value.Value<string>(), out _)) return true;
                    error = "Expected boolean.";
                    return false;
                case "integer":
                    if (value.Type == JTokenType.Integer) return true;
                    if (value.Type == JTokenType.String && long.TryParse(value.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return true;
                    error = "Expected integer.";
                    return false;
                case "number":
                    if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return true;
                    if (value.Type == JTokenType.String && double.TryParse(value.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
                    error = "Expected number.";
                    return false;
                case "array":
                    if (value is JArray array)
                    {
                        var itemSchema = schema["items"] as JObject;
                        if (itemSchema != null)
                        {
                            foreach (var item in array)
                            {
                                if (!ValidateValue(item, itemSchema, out error)) return false;
                            }
                        }
                        return true;
                    }
                    error = "Expected array.";
                    return false;
                case "object":
                    if (value is JObject obj)
                    {
                        return ValidateArguments(obj, schema, out error);
                    }
                    error = "Expected object.";
                    return false;
                default:
                    return true;
            }
        }
    }
}
