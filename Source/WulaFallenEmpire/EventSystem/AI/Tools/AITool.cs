using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public abstract class AITool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string UsageSchema { get; } // JSON schema description

        public virtual string Execute(string args) => "Error: Synchronous execution not supported for this tool.";
        public virtual Task<string> ExecuteAsync(string args) => Task.FromResult(Execute(args));

        /// <summary>
        /// Helper method to parse JSON arguments into a dictionary.
        /// </summary>
        protected Dictionary<string, object> ParseJsonArgs(string json)
        {
            var argsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return argsDict;
            if (JsonToolCallParser.TryParseObject(json, out Dictionary<string, object> parsed))
            {
                return parsed;
            }
            return argsDict;
        }

        protected static bool TryGetString(Dictionary<string, object> args, string key, out string value)
        {
            value = null;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;
            if (args.TryGetValue(key, out object raw) && raw != null)
            {
                value = Convert.ToString(raw, CultureInfo.InvariantCulture);
                return !string.IsNullOrWhiteSpace(value);
            }
            return false;
        }

        protected static bool TryGetInt(Dictionary<string, object> args, string key, out int value)
        {
            value = 0;
            if (!TryGetNumber(args, key, out double number)) return false;
            value = (int)Math.Round(number);
            return true;
        }

        protected static bool TryGetFloat(Dictionary<string, object> args, string key, out float value)
        {
            value = 0f;
            if (!TryGetNumber(args, key, out double number)) return false;
            value = (float)number;
            return true;
        }

        protected static bool TryGetBool(Dictionary<string, object> args, string key, out bool value)
        {
            value = false;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!args.TryGetValue(key, out object raw) || raw == null) return false;
            if (raw is bool b)
            {
                value = b;
                return true;
            }
            if (raw is string s && bool.TryParse(s, out bool parsed))
            {
                value = parsed;
                return true;
            }
            if (raw is long l)
            {
                value = l != 0;
                return true;
            }
            if (raw is double d)
            {
                value = Math.Abs(d) > 0.0001;
                return true;
            }
            return false;
        }

        protected static bool TryGetObject(Dictionary<string, object> args, string key, out Dictionary<string, object> value)
        {
            value = null;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;
            if (args.TryGetValue(key, out object raw) && raw is Dictionary<string, object> dict)
            {
                value = dict;
                return true;
            }
            return false;
        }

        protected static bool TryGetList(Dictionary<string, object> args, string key, out List<object> value)
        {
            value = null;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;
            if (args.TryGetValue(key, out object raw) && raw is List<object> list)
            {
                value = list;
                return true;
            }
            return false;
        }

        protected static bool LooksLikeJson(string input)
        {
            return JsonToolCallParser.LooksLikeJson(input);
        }

        private static bool TryGetNumber(Dictionary<string, object> args, string key, out double value)
        {
            value = 0;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!args.TryGetValue(key, out object raw) || raw == null) return false;
            if (raw is double d)
            {
                value = d;
                return true;
            }
            if (raw is float f)
            {
                value = f;
                return true;
            }
            if (raw is int i)
            {
                value = i;
                return true;
            }
            if (raw is long l)
            {
                value = l;
                return true;
            }
            if (raw is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }
    }
}
