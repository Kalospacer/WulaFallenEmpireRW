using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEngine;
using Verse;
using System.Linq;
using System.Text.RegularExpressions;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class ToolCallRequest
    {
        public string Id;
        public string Name;
        public string ArgumentsJson;
    }

    public sealed class ChatMessage
    {
        public string Role;
        public string Content;
        public string ToolCallId;
        public List<ToolCallRequest> ToolCalls;

        public static ChatMessage User(string content)
        {
            return new ChatMessage { Role = "user", Content = content };
        }

        public static ChatMessage Assistant(string content)
        {
            return new ChatMessage { Role = "assistant", Content = content };
        }

        public static ChatMessage AssistantWithToolCalls(List<ToolCallRequest> toolCalls, string content = null)
        {
            return new ChatMessage { Role = "assistant", Content = content, ToolCalls = toolCalls };
        }

        public static ChatMessage ToolResult(string toolCallId, string content)
        {
            return new ChatMessage { Role = "tool", ToolCallId = toolCallId, Content = content };
        }
    }

    public sealed class ChatCompletionResult
    {
        public string Content;
        public List<ToolCallRequest> ToolCalls;
        public string Thought;
    }

    public class SimpleAIClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly bool _useGemini;
        private const int MaxLogChars = 2000;

        public SimpleAIClient(string apiKey, string baseUrl, string model, bool useGemini = false)
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl?.TrimEnd('/');
            _model = model;
            _useGemini = useGemini;
        }

        public async Task<string> GetChatCompletionAsync(string instruction, List<(string role, string message)> messages, int? maxTokens = null, float? temperature = null, string base64Image = null, string toolChoice = null)
        {
            // 1. Gemini Mode
            if (_useGemini)
            {
                string geminiResponse = await GetGeminiCompletionAsync(instruction, messages, maxTokens, temperature, base64Image);
                
                // Fallback: If failed and had image, retry without image
                if (geminiResponse == null && !string.IsNullOrEmpty(base64Image))
                {
                    WulaLog.Debug("[WulaAI] [WARNING] Visual request failed (likely model incompatible). Retrying text-only...");
                    return await GetGeminiCompletionAsync(instruction, messages, maxTokens, temperature, null);
                }
                return geminiResponse;
            }

            // 2. OpenAI / Compatible Mode
            if (string.IsNullOrEmpty(_baseUrl))
            {
                WulaLog.Debug("[WulaAI] Base URL is missing.");
                return null;
            }

            string endpoint = $"{_baseUrl}/chat/completions";
            if (_baseUrl.EndsWith("/chat/completions")) endpoint = _baseUrl;
            else if (!_baseUrl.EndsWith("/v1")) endpoint = $"{_baseUrl}/v1/chat/completions";

            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("{");
            jsonBuilder.Append($"\"model\": \"{_model}\",");
            jsonBuilder.Append("\"stream\": false,");
            if (maxTokens.HasValue) jsonBuilder.Append($"\"max_tokens\": {Math.Max(1, maxTokens.Value)},");
            if (temperature.HasValue) jsonBuilder.Append($"\"temperature\": {temperature.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
            if (!string.IsNullOrWhiteSpace(toolChoice)) jsonBuilder.Append($"\"tool_choice\": \"{EscapeJson(toolChoice)}\",");
            
            var validMessages = messages.Where(m => 
            {
                string r = (m.role ?? "user").ToLowerInvariant();
                return r != "toolcall";
            }).ToList();

            jsonBuilder.Append("\"messages\": [");
            if (!string.IsNullOrEmpty(instruction))
            {
                jsonBuilder.Append($"{{\"role\": \"system\", \"content\": \"{EscapeJson(instruction)}\"}}");
                if (validMessages.Count > 0) jsonBuilder.Append(",");
            }

            // Find the index of the last user message to attach the image to
            int lastUserIndex = -1;
            if (!string.IsNullOrEmpty(base64Image))
            {
                for (int i = validMessages.Count - 1; i >= 0; i--)
                {
                    string r = (validMessages[i].role ?? "user").ToLowerInvariant();
                    if (r != "ai" && r != "assistant" && r != "tool" && r != "system")
                    {
                        lastUserIndex = i;
                        break;
                    }
                }
            }

            for (int i = 0; i < validMessages.Count; i++)
            {
                var msg = validMessages[i];
                string role = (msg.role ?? "user").ToLowerInvariant();
                if (role == "ai" || role == "assistant") role = "assistant";
                else if (role == "tool") role = "system";
                
                jsonBuilder.Append($"{{\"role\": \"{role}\", ");
                
                if (i == lastUserIndex && !string.IsNullOrEmpty(base64Image))
                {
                    jsonBuilder.Append("\"content\": [");
                    jsonBuilder.Append($"{{\"type\": \"text\", \"text\": \"{EscapeJson(msg.message)}\"}},");
                    jsonBuilder.Append($"{{\"type\": \"image_url\", \"image_url\": {{\"url\": \"data:image/png;base64,{base64Image}\"}}}}");
                    jsonBuilder.Append("]");
                }
                else
                {
                    jsonBuilder.Append($"\"content\": \"{EscapeJson(msg.message)}\"");
                }
                
                jsonBuilder.Append("}");
                if (i < validMessages.Count - 1) jsonBuilder.Append(",");
            }
            jsonBuilder.Append("]}");

            string response = await SendRequestAsync(endpoint, jsonBuilder.ToString(), _apiKey);
            
            // Fallback: If failed and had image, retry without image
            if (response == null && !string.IsNullOrEmpty(base64Image))
            {
                WulaLog.Debug("[WulaAI] [WARNING] Visual request failed (likely model incompatible). Retrying text-only...");
                return await GetChatCompletionAsync(instruction, messages, maxTokens, temperature, null);
            }
            return response;
        }

        public async Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(string instruction, List<ChatMessage> messages, List<Dictionary<string, object>> tools, int? maxTokens = null, float? temperature = null, string toolChoice = null)
        {
            if (_useGemini)
            {
                WulaLog.Debug("[WulaAI] Native tool calling is not supported with Gemini protocol.");
                return null;
            }

            if (string.IsNullOrEmpty(_baseUrl))
            {
                WulaLog.Debug("[WulaAI] Base URL is missing.");
                return null;
            }

            string endpoint = $"{_baseUrl}/chat/completions";
            if (_baseUrl.EndsWith("/chat/completions")) endpoint = _baseUrl;
            else if (!_baseUrl.EndsWith("/v1")) endpoint = $"{_baseUrl}/v1/chat/completions";

            string jsonBody = BuildChatRequestBody(instruction, messages, tools, maxTokens, temperature, toolChoice);
            string response = await SendRequestRawAsync(endpoint, jsonBody, _apiKey);
            if (response == null) return null;

            return ExtractChatCompletionResult(response);
        }

        private async Task<string> GetGeminiCompletionAsync(string instruction, List<(string role, string message)> messages, int? maxTokens = null, float? temperature = null, string base64Image = null)
        {
            // Ensure messages is not empty to avoid Gemini 400 Error (Invalid Argument)
            if (messages == null) messages = new List<(string role, string message)>();
            if (messages.Count == 0)
            {
                // Gemini API 'contents' cannot be empty. We add a dummy prompt to trigger the model.
                messages.Add(("user", "Start."));
            }

            // Gemini API URL
            string baseUrl = _baseUrl;
            if (string.IsNullOrEmpty(baseUrl) || !baseUrl.Contains("googleapis.com"))
            {
                baseUrl = "https://generativelanguage.googleapis.com/v1beta";
            }

            string endpoint = $"{baseUrl}/models/{_model}:generateContent?key={_apiKey}";
            
            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("{");
            
            if (!string.IsNullOrEmpty(instruction))
            {
                jsonBuilder.Append("\"system_instruction\": {\"parts\": [{\"text\": \"" + EscapeJson(instruction) + "\"}]},");
            }

            jsonBuilder.Append("\"contents\": [");
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                string role = (msg.role ?? "user").ToLowerInvariant();
                if (role == "assistant" || role == "ai") role = "model";
                else role = "user";

                jsonBuilder.Append($"{{\"role\": \"{role}\", \"parts\": [");
                jsonBuilder.Append($"{{\"text\": \"{EscapeJson(msg.message)}\"}}");
                
                if (i == messages.Count - 1 && role == "user" && !string.IsNullOrEmpty(base64Image))
                {
                    jsonBuilder.Append($", {{\"inline_data\": {{\"mime_type\": \"image/png\", \"data\": \"{base64Image}\"}}}}");
                }

                jsonBuilder.Append("]}");
                if (i < messages.Count - 1) jsonBuilder.Append(",");
            }
            jsonBuilder.Append("],");

            jsonBuilder.Append("\"generationConfig\": {");
            if (temperature.HasValue) jsonBuilder.Append($"\"temperature\": {temperature.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
            if (maxTokens.HasValue) jsonBuilder.Append($"\"maxOutputTokens\": {maxTokens.Value}");
            else jsonBuilder.Append("\"maxOutputTokens\": 2048");
            jsonBuilder.Append("}");

            jsonBuilder.Append("}");

            return await SendRequestAsync(endpoint, jsonBuilder.ToString(), null);
        }

        private async Task<string> SendRequestAsync(string endpoint, string jsonBody, string apiKey)
        {
            string response = await SendRequestRawAsync(endpoint, jsonBody, apiKey);
            if (response == null) return null;
            return ExtractContent(response);
        }

        private async Task<string> SendRequestRawAsync(string endpoint, string jsonBody, string apiKey)
        {
            if (Prefs.DevMode)
            {
                string logUrl = endpoint;
                if (logUrl.Contains("key="))
                {
                    logUrl = Regex.Replace(logUrl, @"key=[^&]*", "key=[REDACTED]");
                }
                WulaLog.Debug($"[WulaAI] Sending request to {logUrl}");
                
                // Log request body (truncated to avoid spamming base64)
                string logBody = jsonBody;
                if (logBody.Length > 3000) logBody = logBody.Substring(0, 3000) + "... [Truncated]";
                WulaLog.Debug($"[WulaAI] Request Payload:\n{logBody}");
            }

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                }
                request.timeout = 60;

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Delay(50);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errText = request.downloadHandler.text;
                    WulaLog.Debug($"[WulaAI] API Error ({request.responseCode}): {request.error}\nResponse: {TruncateForLog(errText)}");
                    return null;
                }

                string response = request.downloadHandler.text;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Response Body:\n{TruncateForLog(response)}");
                }
                return response;
            }
        }

        private string BuildChatRequestBody(string instruction, List<ChatMessage> messages, List<Dictionary<string, object>> tools, int? maxTokens, float? temperature, string toolChoice)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["stream"] = false
            };

            if (maxTokens.HasValue) body["max_tokens"] = Math.Max(1, maxTokens.Value);
            if (temperature.HasValue) body["temperature"] = temperature.Value;
            if (!string.IsNullOrWhiteSpace(toolChoice)) body["tool_choice"] = toolChoice;

            var messageList = new List<object>();
            if (!string.IsNullOrEmpty(instruction))
            {
                messageList.Add(new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = instruction
                });
            }

            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    if (msg == null) continue;
                    string role = string.IsNullOrWhiteSpace(msg.Role) ? "user" : msg.Role;
                    var entry = new Dictionary<string, object>
                    {
                        ["role"] = role
                    };

                    if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
                    {
                        entry["tool_call_id"] = msg.ToolCallId ?? "";
                        entry["content"] = msg.Content ?? "";
                        messageList.Add(entry);
                        continue;
                    }

                    if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCalls = new List<object>();
                        foreach (var call in msg.ToolCalls)
                        {
                            if (call == null) continue;
                            var callEntry = new Dictionary<string, object>
                            {
                                ["type"] = "function",
                                ["function"] = new Dictionary<string, object>
                                {
                                    ["name"] = call.Name ?? "",
                                    ["arguments"] = call.ArgumentsJson ?? "{}"
                                }
                            };
                            if (!string.IsNullOrWhiteSpace(call.Id))
                            {
                                callEntry["id"] = call.Id;
                            }
                            toolCalls.Add(callEntry);
                        }
                        entry["content"] = string.IsNullOrWhiteSpace(msg.Content) ? null : msg.Content;
                        entry["tool_calls"] = toolCalls;
                        messageList.Add(entry);
                        continue;
                    }

                    entry["content"] = msg.Content ?? "";
                    messageList.Add(entry);
                }
            }

            body["messages"] = messageList;

            if (tools != null && tools.Count > 0)
            {
                var toolList = new List<object>();
                foreach (var tool in tools)
                {
                    if (tool == null) continue;
                    toolList.Add(tool);
                }
                if (toolList.Count > 0)
                {
                    body["tools"] = toolList;
                }
            }

            return JsonToolCallParser.SerializeToJson(body);
        }

        private ChatCompletionResult ExtractChatCompletionResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            if (!JsonToolCallParser.TryParseObject(json, out var root))
            {
                return new ChatCompletionResult { Content = ExtractContent(json) };
            }

            if (!TryGetList(root, "choices", out var choices) || choices.Count == 0)
            {
                return new ChatCompletionResult { Content = ExtractContent(json) };
            }

            var firstChoice = choices[0] as Dictionary<string, object>;
            if (firstChoice == null)
            {
                return new ChatCompletionResult { Content = ExtractContent(json) };
            }

            Dictionary<string, object> message = null;
            if (TryGetObject(firstChoice, "message", out var msgObj))
            {
                message = msgObj;
            }
            else if (TryGetObject(firstChoice, "delta", out var deltaObj))
            {
                message = deltaObj;
            }

            if (message == null)
            {
                return new ChatCompletionResult { Content = ExtractContent(json) };
            }

            string content = TryGetString(message, "content");
            string thought = TryGetString(message, "reasoning_content");
            if (string.IsNullOrWhiteSpace(thought)) thought = TryGetString(message, "thought");
            if (string.IsNullOrWhiteSpace(thought)) thought = TryGetString(message, "reasoning");
            var result = new ChatCompletionResult
            {
                Content = content,
                ToolCalls = ParseToolCalls(message),
                Thought = thought
            };
            return result;
        }

        private static List<ToolCallRequest> ParseToolCalls(Dictionary<string, object> message)
        {
            if (!TryGetList(message, "tool_calls", out var calls) || calls.Count == 0)
            {
                return null;
            }

            var results = new List<ToolCallRequest>();
            foreach (var callObj in calls)
            {
                if (callObj is not Dictionary<string, object> callDict) continue;

                string id = TryGetString(callDict, "id");
                string name = null;
                object argsObj = null;

                if (TryGetObject(callDict, "function", out var fnObj))
                {
                    name = TryGetString(fnObj, "name");
                    TryGetValue(fnObj, "arguments", out argsObj);
                }
                else
                {
                    name = TryGetString(callDict, "name");
                    TryGetValue(callDict, "arguments", out argsObj);
                }

                if (string.IsNullOrWhiteSpace(name)) continue;

                string argsJson = "{}";
                if (argsObj is string argsString)
                {
                    argsJson = string.IsNullOrWhiteSpace(argsString) ? "{}" : argsString;
                }
                else if (argsObj is Dictionary<string, object> argsDict)
                {
                    argsJson = JsonToolCallParser.SerializeToJson(argsDict);
                }
                else if (argsObj != null)
                {
                    argsJson = JsonToolCallParser.SerializeToJson(argsObj);
                }

                results.Add(new ToolCallRequest
                {
                    Id = id,
                    Name = name,
                    ArgumentsJson = argsJson
                });
            }

            return results.Count > 0 ? results : null;
        }

        private static bool TryGetList(Dictionary<string, object> obj, string key, out List<object> list)
        {
            list = null;
            if (!TryGetValue(obj, key, out object raw)) return false;
            if (raw is List<object> rawList)
            {
                list = rawList;
                return true;
            }
            return false;
        }

        private static bool TryGetObject(Dictionary<string, object> obj, string key, out Dictionary<string, object> value)
        {
            value = null;
            if (!TryGetValue(obj, key, out object raw)) return false;
            if (raw is Dictionary<string, object> dict)
            {
                value = dict;
                return true;
            }
            return false;
        }

        private static string TryGetString(Dictionary<string, object> obj, string key)
        {
            if (TryGetValue(obj, key, out object value) && value != null)
            {
                return Convert.ToString(value);
            }
            return null;
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

        private string ExtractContent(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            // Handle SSE Stream (data: ...)
            // Some endpoints return SSE streams even if stream=false is requested.
            // We strip 'data:' prefix and aggregate the content deltas.
            if (json.TrimStart().StartsWith("data:"))
            {
                StringBuilder sb = new StringBuilder();
                string[] lines = json.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("data:") && !trimmed.Contains("[DONE]"))
                    {
                        string chunkJson = trimmed.Substring(5).Trim();
                        // Extract content from this chunk
                        string chunkContent = ExtractContentFromSingleJson(chunkJson);
                        if (!string.IsNullOrEmpty(chunkContent))
                        {
                            sb.Append(chunkContent);
                        }
                    }
                }
                return sb.ToString();
            }

            return ExtractContentFromSingleJson(json);
        }

        private string ExtractContentFromSingleJson(string json)
        {
            try
            {
                // 1. Gemini format
                if (json.Contains("\"candidates\""))
                {
                    int partsIndex = json.IndexOf("\"parts\"", StringComparison.Ordinal);
                    if (partsIndex != -1) return ExtractJsonValue(json, "text", partsIndex);
                }

                // 2. OpenAI format
                if (json.Contains("\"choices\""))
                {
                    int choicesIndex = json.IndexOf("\"choices\"", StringComparison.Ordinal);
                    string firstChoice = TryExtractFirstChoiceObject(json, choicesIndex);
                    if (!string.IsNullOrEmpty(firstChoice))
                    {
                        int messageIndex = firstChoice.IndexOf("\"message\"", StringComparison.Ordinal);
                        if (messageIndex != -1) return ExtractJsonValue(firstChoice, "content", messageIndex);
                        
                        int deltaIndex = firstChoice.IndexOf("\"delta\"", StringComparison.Ordinal);
                        if (deltaIndex != -1) return ExtractJsonValue(firstChoice, "content", deltaIndex);

                        return ExtractJsonValue(firstChoice, "text", 0);
                    }
                }

                // 3. Last fallback
                return ExtractJsonValue(json, "content");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Error parsing response: {ex.Message}");
            }
            return null;
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
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string ExtractJsonValue(string json, string key, int startIndex = 0)
        {
            string keyPattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(keyPattern, startIndex, StringComparison.Ordinal);
            if (keyIndex == -1) return null;

            int colonIndex = json.IndexOf(':', keyIndex + keyPattern.Length);
            if (colonIndex == -1) return null;

            int valueStart = json.IndexOf('"', colonIndex);
            if (valueStart == -1) return null;

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
                    else if (c == 'u')
                    {
                        // Handle Unicode escape sequence \uXXXX
                        if (i + 4 < json.Length)
                        {
                            string hex = json.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int charCode))
                            {
                                sb.Append((char)charCode);
                                i += 4;
                            }
                            else
                            {
                                // Fallback if parsing fails
                                sb.Append("\\u");
                                sb.Append(hex);
                                i += 4;
                            }
                        }
                        else
                        {
                            sb.Append("\\u");
                        }
                    }
                    else if (c == '/')
                    {
                         sb.Append('/');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    escaped = false;
                }
                else
                {
                    if (c == '\\') escaped = true;
                    else if (c == '"') return sb.ToString();
                    else sb.Append(c);
                }
            }
            return null;
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
                    default:
                        if (c < 0x20) { sb.Append("\\u"); sb.Append(((int)c).ToString("x4")); }
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string TruncateForLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length <= MaxLogChars) return s;
            return s.Substring(0, MaxLogChars) + "... (truncated)";
        }
    }
}
