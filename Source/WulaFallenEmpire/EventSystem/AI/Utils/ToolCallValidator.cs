using System;
using System.Collections.Generic;
using System.Globalization;
using WulaFallenEmpire.EventSystem.AI.Tools;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    public static class ToolCallValidator
    {
        public static bool TryValidate(AITool tool, string argsJson, out Dictionary<string, object> args, out string error)
        {
            args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            error = null;
            if (tool == null)
            {
                error = "Error: Tool not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(argsJson)) argsJson = "{}";
            if (!JsonToolCallParser.TryParseObject(argsJson, out var parsed))
            {
                error = $"Error: Invalid JSON arguments for tool '{tool.Name}'.";
                return false;
            }

            args = parsed ?? args;
            var schema = ToolSchemaSanitizer.Sanitize(tool.GetParametersSchema());
            if (!TryValidateObject(args, schema, out error))
            {
                error = $"Error: Tool '{tool.Name}' arguments failed validation. {error}";
                return false;
            }

            return true;
        }

        private static bool TryValidateObject(Dictionary<string, object> args, Dictionary<string, object> schema, out string error)
        {
            error = null;
            if (schema == null) return true;

            if (schema.TryGetValue("properties", out object propsObj) && propsObj is Dictionary<string, object> props)
            {
                if (schema.TryGetValue("required", out object requiredObj) && requiredObj is List<object> requiredList)
                {
                    foreach (var req in requiredList)
                    {
                        string name = req as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!args.ContainsKey(name))
                        {
                            error = $"Missing required field '{name}'.";
                            return false;
                        }
                    }
                }

                if (schema.TryGetValue("additionalProperties", out object additionalObj)
                    && additionalObj is bool allowAdditional && !allowAdditional)
                {
                    foreach (var key in args.Keys)
                    {
                        if (!props.ContainsKey(key))
                        {
                            error = $"Unexpected field '{key}'.";
                            return false;
                        }
                    }
                }

                foreach (var entry in args)
                {
                    if (!props.TryGetValue(entry.Key, out object propSchemaObj)) continue;
                    if (propSchemaObj is not Dictionary<string, object> propSchema) continue;
                    if (!TryValidateValue(entry.Value, propSchema, out error))
                    {
                        error = $"Field '{entry.Key}' is invalid. {error}";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryValidateValue(object value, Dictionary<string, object> schema, out string error)
        {
            error = null;
            if (schema == null) return true;

            string type = NormalizeType(schema);
            if (string.IsNullOrWhiteSpace(type)) return true;

            if (value == null)
            {
                error = "Value must not be null.";
                return false;
            }

            switch (type)
            {
                case "string":
                    if (value is string) return true;
                    error = "Expected string.";
                    return false;
                case "boolean":
                    if (value is bool) return true;
                    if (value is string s && bool.TryParse(s, out _)) return true;
                    if (IsNumber(value)) return true;
                    error = "Expected boolean.";
                    return false;
                case "number":
                    if (IsNumber(value)) return true;
                    if (value is string n && double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
                    error = "Expected number.";
                    return false;
                case "integer":
                    if (value is int || value is long) return true;
                    if (value is string i && long.TryParse(i, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return true;
                    error = "Expected integer.";
                    return false;
                case "object":
                    if (value is Dictionary<string, object> obj)
                    {
                        return TryValidateObject(obj, schema, out error);
                    }
                    error = "Expected object.";
                    return false;
                case "array":
                    if (value is List<object> list)
                    {
                        if (schema.TryGetValue("items", out object itemsObj) && itemsObj is Dictionary<string, object> itemSchema)
                        {
                            foreach (var item in list)
                            {
                                if (!TryValidateValue(item, itemSchema, out error)) return false;
                            }
                        }
                        return true;
                    }
                    error = "Expected array.";
                    return false;
                default:
                    return true;
            }
        }

        private static string NormalizeType(Dictionary<string, object> schema)
        {
            if (!schema.TryGetValue("type", out object typeObj) || typeObj == null) return null;
            if (typeObj is string s) return s;
            if (typeObj is List<object> list)
            {
                foreach (var item in list)
                {
                    if (item is string candidate && !string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static bool IsNumber(object value)
        {
            return value is int || value is long || value is float || value is double || value is decimal;
        }
    }
}
