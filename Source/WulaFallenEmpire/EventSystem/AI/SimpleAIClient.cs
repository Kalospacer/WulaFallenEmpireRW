using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class SimpleAIClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public SimpleAIClient(string apiKey, string baseUrl, string model)
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl?.TrimEnd('/');
            _model = model;
        }

        public async Task<ApiResponse> GetChatCompletionAsync(string instruction, List<ApiMessage> messages)
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                Log.Error("[WulaAI] Base URL is missing.");
                return null;
            }

            string endpoint = $"{_baseUrl}/chat/completions";
            if (_baseUrl.EndsWith("/chat/completions")) endpoint = _baseUrl;
            else if (!_baseUrl.EndsWith("/v1")) endpoint = $"{_baseUrl}/v1/chat/completions";

            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("{");
            jsonBuilder.Append($"\"model\": \"{_model}\",");
            jsonBuilder.Append("\"stream\": false,");
            jsonBuilder.Append("\"messages\": [");
            
            if (!string.IsNullOrEmpty(instruction))
            {
                jsonBuilder.Append($"{{\"role\": \"system\", \"content\": \"{EscapeJson(instruction)}\"}},");
            }

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                string role = msg.role.ToLower();
                if (role == "ai") role = "assistant";

                jsonBuilder.Append("{");
                jsonBuilder.Append($"\"role\": \"{role}\"");

                if (!string.IsNullOrEmpty(msg.content))
                {
                    jsonBuilder.Append($", \"content\": \"{EscapeJson(msg.content)}\"");
                }

                if (msg.tool_calls != null && msg.tool_calls.Any())
                {
                    jsonBuilder.Append(", \"tool_calls\": [");
                    for (int j = 0; j < msg.tool_calls.Count; j++)
                    {
                        var toolCall = msg.tool_calls[j];
                        jsonBuilder.Append("{");
                        jsonBuilder.Append($"\"id\": \"{toolCall.id}\",");
                        jsonBuilder.Append($"\"type\": \"{toolCall.type}\",");
                        jsonBuilder.Append($"\"function\": {{ \"name\": \"{toolCall.function.name}\", \"arguments\": \"{EscapeJson(toolCall.function.arguments)}\" }}");
                        jsonBuilder.Append("}");
                        if (j < msg.tool_calls.Count - 1) jsonBuilder.Append(",");
                    }
                    jsonBuilder.Append("]");
                }

                if (!string.IsNullOrEmpty(msg.tool_call_id))
                {
                    jsonBuilder.Append($", \"tool_call_id\": \"{msg.tool_call_id}\"");
                }

                jsonBuilder.Append("}");
                if (i < messages.Count - 1) jsonBuilder.Append(",");
            }
            
            jsonBuilder.Append("]");
            jsonBuilder.Append("}");

            string jsonBody = jsonBuilder.ToString();
            Log.Message($"[WulaAI] Sending request: {jsonBody}");
            
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
                    Log.Error($"[WulaAI] API Error: {request.error}\nResponse: {request.downloadHandler.text}");
                    return null;
                }

                string responseText = request.downloadHandler.text;
                Log.Message($"[WulaAI] Received raw response: {responseText}");
                return ParseApiResponse(responseText);
            }
        }

        private string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private ApiResponse ParseApiResponse(string json)
        {
            try
            {
                var parsed = SimpleJsonParser.Parse(json);
                if (parsed.TryGetValue("choices", out string choicesStr))
                {
                    var choices = SimpleJsonParser.ParseArray(choicesStr);
                    if (choices.Any())
                    {
                        var firstChoice = choices.First();
                        if (firstChoice.TryGetValue("message", out string messageStr))
                        {
                            var message = SimpleJsonParser.Parse(messageStr);
                            string content = null;
                            if (message.TryGetValue("content", out string c)) content = c;

                            List<ToolCall> toolCalls = new List<ToolCall>();
                            if (message.TryGetValue("tool_calls", out string toolCallsStr))
                            {
                                var toolCallArray = SimpleJsonParser.ParseArray(toolCallsStr);
                                foreach (var tc in toolCallArray)
                                {
                                    if (tc.TryGetValue("id", out string id) &&
                                        tc.TryGetValue("type", out string type) &&
                                        tc.TryGetValue("function", out string functionStr))
                                    {
                                        var function = SimpleJsonParser.Parse(functionStr);
                                        if (function.TryGetValue("name", out string name) &&
                                            function.TryGetValue("arguments", out string args))
                                        {
                                            toolCalls.Add(new ToolCall { id = id, type = type, function = new ToolFunction { name = name, arguments = args } });
                                        }
                                    }
                                }
                            }
                            return new ApiResponse { content = content, tool_calls = toolCalls };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaAI] Error parsing API response: {ex}");
            }
            return null;
        }
    }

    public class ApiMessage
    {
        public string role;
        public string content;
        public List<ToolCall> tool_calls;
        public string tool_call_id; // For tool responses
    }

    public class ToolCall
    {
        public string id;
        public string type; // "function"
        public ToolFunction function;
    }

    public class ToolFunction
    {
        public string name;
        public string arguments; // JSON string
    }

    public class ApiResponse
    {
        public string content;
        public List<ToolCall> tool_calls;
    }
}