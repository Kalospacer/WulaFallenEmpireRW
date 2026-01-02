using System;
using System.Collections.Generic;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    public static class ToolSchemaSanitizer
    {
        public static Dictionary<string, object> Sanitize(Dictionary<string, object> schema)
        {
            if (schema == null) return new Dictionary<string, object>();

            string schemaType = NormalizeType(schema);
            if (string.IsNullOrWhiteSpace(schemaType))
            {
                schemaType = "object";
                schema["type"] = schemaType;
            }

            if (string.Equals(schemaType, "object", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetDict(schema, "properties", out var props))
                {
                    props = new Dictionary<string, object>();
                    schema["properties"] = props;
                }

                var sanitizedProps = new Dictionary<string, object>();
                foreach (var entry in props)
                {
                    if (entry.Value is Dictionary<string, object> child)
                    {
                        sanitizedProps[entry.Key] = Sanitize(child);
                    }
                    else
                    {
                        sanitizedProps[entry.Key] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        };
                    }
                }
                schema["properties"] = sanitizedProps;

                if (!schema.ContainsKey("additionalProperties"))
                {
                    schema["additionalProperties"] = false;
                }

                if (schema.TryGetValue("required", out object requiredRaw) && requiredRaw is List<object> requiredList)
                {
                    var filtered = new List<object>();
                    foreach (var item in requiredList)
                    {
                        string name = item as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (sanitizedProps.ContainsKey(name))
                        {
                            filtered.Add(name);
                        }
                    }
                    schema["required"] = filtered;
                }
            }
            else if (string.Equals(schemaType, "array", StringComparison.OrdinalIgnoreCase))
            {
                if (schema.TryGetValue("items", out object itemsObj) && itemsObj is Dictionary<string, object> itemSchema)
                {
                    schema["items"] = Sanitize(itemSchema);
                }
            }

            return schema;
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
                        schema["type"] = candidate;
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static bool TryGetDict(Dictionary<string, object> root, string key, out Dictionary<string, object> value)
        {
            value = null;
            if (root == null || string.IsNullOrWhiteSpace(key)) return false;
            if (root.TryGetValue(key, out object raw) && raw is Dictionary<string, object> dict)
            {
                value = dict;
                return true;
            }
            return false;
        }
    }
}
