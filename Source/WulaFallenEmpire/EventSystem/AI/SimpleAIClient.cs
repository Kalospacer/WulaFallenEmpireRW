using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class SimpleAIClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;
        private const int MaxLogChars = 2000;

        public SimpleAIClient(string apiKey, string baseUrl, string model)
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl?.TrimEnd('/');
            _model = model;
        }

        public async Task<string> GetChatCompletionAsync(string instruction, List<(string role, string message)> messages, int? maxTokens = null, float? temperature = null)
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                WulaLog.Debug("[WulaAI] Base URL is missing.");
                return null;
            }

            string endpoint = $"{_baseUrl}/chat/completions";
            // Handle cases where baseUrl already includes /v1 or full path
            if (_baseUrl.EndsWith("/chat/completions")) endpoint = _baseUrl;
            else if (!_baseUrl.EndsWith("/v1")) endpoint = $"{_baseUrl}/v1/chat/completions";

            // Build JSON manually to avoid dependencies
            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("{");
            jsonBuilder.Append($"\"model\": \"{_model}\",");
            jsonBuilder.Append("\"stream\": false,"); // We request non-stream, but handle stream if returned
            if (maxTokens.HasValue)
            {
                jsonBuilder.Append($"\"max_tokens\": {Math.Max(1, maxTokens.Value)},");
            }
            if (temperature.HasValue)
            {
                float clamped = Mathf.Clamp(temperature.Value, 0f, 2f);
                jsonBuilder.Append($"\"temperature\": {clamped.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
            }
            jsonBuilder.Append("\"messages\": [");
            
            // System instruction
            bool firstMessage = true;
            if (!string.IsNullOrEmpty(instruction))
            {
                jsonBuilder.Append($"{{\"role\": \"system\", \"content\": \"{EscapeJson(instruction)}\"}}");
                firstMessage = false;
            }

            // Messages
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                string role = (msg.role ?? "user").ToLowerInvariant();
                if (role == "ai") role = "assistant";
                else if (role == "tool") role = "system"; // Internal-only role; map to supported role for Chat Completions APIs.
                else if (role != "system" && role != "user" && role != "assistant") role = "user";
                
                if (!firstMessage) jsonBuilder.Append(",");
                jsonBuilder.Append($"{{\"role\": \"{role}\", \"content\": \"{EscapeJson(msg.message)}\"}}");
                firstMessage = false;
            }
            
            jsonBuilder.Append("]");
            jsonBuilder.Append("}");

            string jsonBody = jsonBuilder.ToString();
            if (Prefs.DevMode)
            {
                WulaLog.Debug($"[WulaAI] Sending request to {endpoint} (model={_model}, messages={messages?.Count ?? 0})");
                WulaLog.Debug($"[WulaAI] Request body (truncated):\n{TruncateForLog(jsonBody)}");
            }

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                }

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Delay(50);
                }

                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    WulaLog.Debug($"[WulaAI] API Error: {request.error}\nResponse (truncated): {TruncateForLog(request.downloadHandler.text)}");
                    return null;
                }

                string responseText = request.downloadHandler.text;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Raw Response (truncated): {TruncateForLog(responseText)}");
                }
                return ExtractContent(responseText);
            }
        }

        private static string TruncateForLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length <= MaxLogChars) return s;
            return s.Substring(0, MaxLogChars) + $"... (truncated, total {s.Length} chars)";
        }

        private string EscapeJson(string s)
        {
            if (s == null) return "";

            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
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

        private string ExtractContent(string json)
        {
            try
            {
                // Check for stream format (SSE)
                // SSE lines start with "data: "
                if (json.TrimStart().StartsWith("data:"))
                {
                    StringBuilder fullContent = new StringBuilder();
                    string[] lines = json.Split(new[] { "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                            string trimmedLine = line.Trim();
                            if (trimmedLine == "data: [DONE]") continue;
                            if (trimmedLine.StartsWith("data: "))
                            {
                                string dataJson = trimmedLine.Substring(6);
                            // Extract content from this chunk
                            string chunkContent = TryExtractAssistantContent(dataJson) ?? ExtractJsonValue(dataJson, "content");
                            if (!string.IsNullOrEmpty(chunkContent))
                            {
                                fullContent.Append(chunkContent);
                            }
                        }
                    }
                    return fullContent.ToString();
                }
                else
                {
                    // Standard non-stream format
                    return TryExtractAssistantContent(json) ?? ExtractJsonValue(json, "content");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Error parsing response: {ex}");
            }
            return null;
        }

        private static string TryExtractAssistantContent(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            int choicesIndex = json.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choicesIndex == -1) return null;

            string firstChoiceJson = TryExtractFirstChoiceObject(json, choicesIndex);
            if (string.IsNullOrEmpty(firstChoiceJson)) return null;

            int messageIndex = firstChoiceJson.IndexOf("\"message\"", StringComparison.Ordinal);
            if (messageIndex != -1)
            {
                return ExtractJsonValue(firstChoiceJson, "content", messageIndex);
            }

            int deltaIndex = firstChoiceJson.IndexOf("\"delta\"", StringComparison.Ordinal);
            if (deltaIndex != -1)
            {
                return ExtractJsonValue(firstChoiceJson, "content", deltaIndex);
            }

            return ExtractJsonValue(firstChoiceJson, "text", 0);
        }

        private static string TryExtractFirstChoiceObject(string json, int choicesKeyIndex)
        {
            int arrayStart = json.IndexOf('[', choicesKeyIndex);
            if (arrayStart == -1) return null;

            int objStart = json.IndexOf('{', arrayStart);
            if (objStart == -1) return null;

            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd == -1) return null;

            return json.Substring(objStart, objEnd - objStart + 1);
        }

        private static int FindMatchingBrace(string json, int startIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static string ExtractJsonValue(string json, string key)
        {
            // Simple parser to find "key": "value"
            // This is not a full JSON parser and assumes standard formatting
            return ExtractJsonValue(json, key, 0);
        }

        private static string ExtractJsonValue(string json, string key, int startIndex)
        {
            string keyPattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(keyPattern, startIndex, StringComparison.Ordinal);
            if (keyIndex == -1) return null;

            // Find the colon after the key
            int colonIndex = json.IndexOf(':', keyIndex + keyPattern.Length);
            if (colonIndex == -1) return null;

            // Find the opening quote of the value
            int valueStart = json.IndexOf('"', colonIndex);
            if (valueStart == -1) return null;

            // Extract string with escape handling
            StringBuilder sb = new StringBuilder();
            bool escaped = false;
            for (int i = valueStart + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    if (c == 'n') sb.Append('\n');
                    else if (c == 'r') sb.Append('\r');
                    else if (c == 't') sb.Append('\t');
                    else if (c == '"') sb.Append('"');
                    else if (c == '\\') sb.Append('\\');
                    else sb.Append(c); // Literal
                    escaped = false;
                }
                else
                {
                    if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        // End of string
                        return sb.ToString();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            return null;
        }
    }
}
