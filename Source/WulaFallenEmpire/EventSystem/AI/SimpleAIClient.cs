using System;
using System.Collections.Generic;
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

        public async Task<string> GetChatCompletionAsync(string instruction, List<(string role, string message)> messages)
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                Log.Error("[WulaAI] Base URL is missing.");
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
            jsonBuilder.Append("\"stream\": false,");
            jsonBuilder.Append("\"messages\": [");
            
            // System instruction
            if (!string.IsNullOrEmpty(instruction))
            {
                jsonBuilder.Append($"{{\"role\": \"system\", \"content\": \"{EscapeJson(instruction)}\"}},");
            }

            // Messages
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                string role = msg.role.ToLower();
                if (role == "ai") role = "assistant";
                // Map other roles if needed
                
                jsonBuilder.Append($"{{\"role\": \"{role}\", \"content\": \"{EscapeJson(msg.message)}\"}}");
                if (i < messages.Count - 1) jsonBuilder.Append(",");
            }
            
            jsonBuilder.Append("]");
            jsonBuilder.Append("}");

            string jsonBody = jsonBuilder.ToString();
            Log.Message($"[WulaAI] Sending request to {endpoint}:\n{jsonBody}");

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
                Log.Message($"[WulaAI] Raw Response: {responseText}");
                return ExtractContent(responseText);
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

        private string ExtractContent(string json)
        {
            try
            {
                // Robust parsing for "content": "..." allowing for whitespace variations
                int contentIndex = json.IndexOf("\"content\"");
                if (contentIndex == -1) return null;

                // Find the opening quote after "content"
                int openQuoteIndex = -1;
                for (int i = contentIndex + 9; i < json.Length; i++)
                {
                    if (json[i] == '"')
                    {
                        openQuoteIndex = i;
                        break;
                    }
                }
                if (openQuoteIndex == -1) return null;

                int startIndex = openQuoteIndex + 1;
                StringBuilder content = new StringBuilder();
                bool escaped = false;
                
                for (int i = startIndex; i < json.Length; i++)
                {
                    char c = json[i];
                    if (escaped)
                    {
                        if (c == 'n') content.Append('\n');
                        else if (c == 'r') content.Append('\r');
                        else if (c == 't') content.Append('\t');
                        else if (c == '"') content.Append('"');
                        else if (c == '\\') content.Append('\\');
                        else content.Append(c); // Literal
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
                            return content.ToString();
                        }
                        else
                        {
                            content.Append(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaAI] Error parsing response: {ex}");
            }
            return null;
        }
    }
}