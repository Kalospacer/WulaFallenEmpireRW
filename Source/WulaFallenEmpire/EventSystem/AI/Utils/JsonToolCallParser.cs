using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    public sealed class ToolCallInfo
    {
        public string Id;
        public string Name;
        public Dictionary<string, object> Arguments;
        public string ArgumentsJson;
    }

    public static class JsonToolCallParser
    {
        public static bool TryParseToolCalls(string input, out List<ToolCallInfo> toolCalls)
        {
            toolCalls = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (!TryParseValue(input, out object root)) return false;
            if (root is not Dictionary<string, object> obj) return false;

            if (!TryGetValue(obj, "tool_calls", out object callsObj)) return false;
            if (callsObj is not List<object> callsList) return false;

            var parsedCalls = new List<ToolCallInfo>();
            foreach (var entry in callsList)
            {
                if (entry is not Dictionary<string, object> callObj) continue;

                string id = TryGetString(callObj, "id");
                string name = null;
                object argsObj = null;

                if (TryGetValue(callObj, "function", out object fnObj) && fnObj is Dictionary<string, object> fnDict)
                {
                    name = TryGetString(fnDict, "name");
                    TryGetValue(fnDict, "arguments", out argsObj);
                }
                else
                {
                    name = TryGetString(callObj, "name");
                    TryGetValue(callObj, "arguments", out argsObj);
                }

                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!TryNormalizeArguments(argsObj, out Dictionary<string, object> args, out string argsJson))
                {
                    args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    argsJson = "{}";
                }

                parsedCalls.Add(new ToolCallInfo
                {
                    Id = id,
                    Name = name.Trim(),
                    Arguments = args,
                    ArgumentsJson = argsJson
                });
            }

            toolCalls = parsedCalls;
            return true;
        }

        public static bool TryParseToolCallsFromText(string input, out List<ToolCallInfo> toolCalls, out string jsonFragment)
        {
            toolCalls = null;
            jsonFragment = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string trimmed = input.Trim();
            if (TryParseToolCalls(trimmed, out toolCalls))
            {
                jsonFragment = trimmed;
                return true;
            }

            int firstBrace = trimmed.IndexOf('{');
            int lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                string candidate = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
                if (TryParseToolCalls(candidate, out toolCalls))
                {
                    jsonFragment = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TryParseObject(string json, out Dictionary<string, object> obj)
        {
            obj = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            if (!TryParseValue(json, out object value)) return false;
            if (value is not Dictionary<string, object> dict) return false;
            obj = dict;
            return true;
        }

        public static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        public static string SerializeToJson(object value)
        {
            var sb = new StringBuilder();
            AppendValue(sb, value);
            return sb.ToString();
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                sb.Append('\"').Append(EscapeJson(s)).Append('\"');
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is double d)
            {
                sb.Append(d.ToString("0.################", CultureInfo.InvariantCulture));
                return;
            }

            if (value is float f)
            {
                sb.Append(f.ToString("0.################", CultureInfo.InvariantCulture));
                return;
            }

            if (value is int or long or short or byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is Dictionary<string, object> obj)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kvp in obj)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('\"').Append(EscapeJson(kvp.Key)).Append('\"').Append(':');
                    AppendValue(sb, kvp.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is List<object> list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendValue(sb, list[i]);
                }
                sb.Append(']');
                return;
            }

            sb.Append('\"').Append(EscapeJson(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")).Append('\"');
        }

        private static bool TryNormalizeArguments(object argsObj, out Dictionary<string, object> args, out string argsJson)
        {
            args = null;
            argsJson = null;

            if (argsObj == null)
            {
                args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                argsJson = "{}";
                return true;
            }

            if (argsObj is Dictionary<string, object> dict)
            {
                args = dict;
                argsJson = SerializeToJson(dict);
                return true;
            }

            if (argsObj is string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    argsJson = "{}";
                    return true;
                }

                if (TryParseObject(s, out Dictionary<string, object> parsed))
                {
                    args = parsed;
                    argsJson = SerializeToJson(parsed);
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool TryParseValue(string json, out object value)
        {
            value = null;
            var reader = new JsonReader(json);
            if (!reader.TryReadValue(out value)) return false;
            reader.SkipWhitespace();
            return reader.IsAtEnd;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static bool TryGetValue(Dictionary<string, object> obj, string key, out object value)
        {
            if (obj == null)
            {
                value = null;
                return false;
            }
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private static string TryGetString(Dictionary<string, object> obj, string key)
        {
            if (TryGetValue(obj, key, out object value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            return null;
        }

        private sealed class JsonReader
        {
            private readonly string _text;
            private int _index;

            public JsonReader(string text)
            {
                _text = text ?? "";
                _index = 0;
            }

            public bool IsAtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                }
            }

            public bool TryReadValue(out object value)
            {
                value = null;
                SkipWhitespace();
                if (IsAtEnd) return false;

                char c = _text[_index];
                if (c == '{') return TryReadObject(out value);
                if (c == '[') return TryReadArray(out value);
                if (c == '\"') return TryReadString(out value);
                if (c == '-' || char.IsDigit(c)) return TryReadNumber(out value);
                if (TryReadLiteral("true")) { value = true; return true; }
                if (TryReadLiteral("false")) { value = false; return true; }
                if (TryReadLiteral("null")) { value = null; return true; }
                return false;
            }

            private bool TryReadObject(out object value)
            {
                value = null;
                if (!TryReadChar('{')) return false;
                SkipWhitespace();

                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (TryReadChar('}'))
                {
                    value = dict;
                    return true;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (!TryReadString(out object keyObj)) return false;
                    string key = keyObj as string ?? "";
                    SkipWhitespace();
                    if (!TryReadChar(':')) return false;
                    if (!TryReadValue(out object itemValue)) return false;
                    dict[key] = itemValue;
                    SkipWhitespace();
                    if (TryReadChar('}'))
                    {
                        value = dict;
                        return true;
                    }
                    if (!TryReadChar(',')) return false;
                }
            }

            private bool TryReadArray(out object value)
            {
                value = null;
                if (!TryReadChar('[')) return false;
                SkipWhitespace();

                var list = new List<object>();
                if (TryReadChar(']'))
                {
                    value = list;
                    return true;
                }

                while (true)
                {
                    if (!TryReadValue(out object item)) return false;
                    list.Add(item);
                    SkipWhitespace();
                    if (TryReadChar(']'))
                    {
                        value = list;
                        return true;
                    }
                    if (!TryReadChar(',')) return false;
                }
            }

            private bool TryReadString(out object value)
            {
                value = null;
                if (!TryReadChar('\"')) return false;
                var sb = new StringBuilder();
                while (_index < _text.Length)
                {
                    char c = _text[_index++];
                    if (c == '\"')
                    {
                        value = sb.ToString();
                        return true;
                    }
                    if (c == '\\')
                    {
                        if (_index >= _text.Length) return false;
                        char esc = _text[_index++];
                        switch (esc)
                        {
                            case '\"': sb.Append('\"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_index + 4 > _text.Length) return false;
                                string hex = _text.Substring(_index, 4);
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    return false;
                                }
                                sb.Append((char)code);
                                _index += 4;
                                break;
                            default:
                                return false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return false;
            }

            private bool TryReadNumber(out object value)
            {
                value = null;
                int start = _index;
                if (_text[_index] == '-') _index++;

                while (_index < _text.Length && char.IsDigit(_text[_index]))
                {
                    _index++;
                }

                bool hasDot = false;
                if (_index < _text.Length && _text[_index] == '.')
                {
                    hasDot = true;
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        _index++;
                    }
                }

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    hasDot = true;
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                    {
                        _index++;
                    }
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        _index++;
                    }
                }

                string number = _text.Substring(start, _index - start);
                if (!hasDot && long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
                {
                    value = longVal;
                    return true;
                }

                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
                {
                    value = dbl;
                    return true;
                }

                return false;
            }

            private bool TryReadLiteral(string literal)
            {
                SkipWhitespace();
                if (_text.Length - _index < literal.Length) return false;
                if (string.Compare(_text, _index, literal, 0, literal.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
                _index += literal.Length;
                return true;
            }

            private bool TryReadChar(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length) return false;
                if (_text[_index] != expected) return false;
                _index++;
                return true;
            }
        }
    }
}
