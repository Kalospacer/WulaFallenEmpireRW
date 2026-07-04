using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class GeminiProvider : IAIProvider
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public GeminiProvider(string apiKey, string baseUrl, string model)
        {
            _apiKey = apiKey;
            _baseUrl = AIProviderJson.NormalizeBaseUrl(baseUrl, "https://generativelanguage.googleapis.com/v1beta");
            _model = model;
        }

        public async Task<AIProviderResponse> SendAsync(AIProviderRequest request, CancellationToken cancellationToken)
        {
            string json = await PostAsync(request, false, cancellationToken);
            return ParseGenerateContent(json);
        }

        public async Task<AIProviderResponse> StreamAsync(AIProviderRequest request, Action<AIStreamEvent> onEvent, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request);
            using (var httpRequest = BuildHttpRequest(payload, true))
            using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                string bodyIfError = null;
                if (!response.IsSuccessStatusCode)
                {
                    bodyIfError = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Gemini API error {(int)response.StatusCode}: {bodyIfError}");
                }

                var accumulator = new GeminiStreamAccumulator();
                await AIProviderJson.ReadSseAsync(response, (eventName, data) =>
                {
                    ParseStreamChunk(data, accumulator, onEvent);
                }, cancellationToken);
                onEvent?.Invoke(new AIStreamEvent { Completed = true });
                return accumulator.ToResponse();
            }
        }

        private async Task<string> PostAsync(AIProviderRequest request, bool stream, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request);
            using (var httpRequest = BuildHttpRequest(payload, stream))
            using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, cancellationToken))
            {
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Gemini API error {(int)response.StatusCode}: {body}");
                }
                return body;
            }
        }

        private HttpRequestMessage BuildHttpRequest(JObject payload, bool stream)
        {
            string action = stream ? "streamGenerateContent?alt=sse" : "generateContent";
            string endpoint = $"{_baseUrl}/models/{Uri.EscapeDataString(_model)}:{action}";
            endpoint += endpoint.Contains("?") ? "&" : "?";
            endpoint += "key=" + Uri.EscapeDataString(_apiKey ?? string.Empty);
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = AIProviderJson.JsonContent(payload);
            return request;
        }

        private JObject BuildPayload(AIProviderRequest request)
        {
            var payload = new JObject();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                payload["system_instruction"] = new JObject
                {
                    ["parts"] = new JArray { new JObject { ["text"] = request.SystemPrompt } }
                };
            }

            var contents = new JArray();
            foreach (var message in request.Messages ?? new List<AIMessage>())
            {
                if (message == null) continue;
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var converted = ConvertMessage(message);
                if (converted != null) contents.Add(converted);
            }
            if (contents.Count == 0)
            {
                contents.Add(new JObject
                {
                    ["role"] = "user",
                    ["parts"] = new JArray { new JObject { ["text"] = "Start." } }
                });
            }
            payload["contents"] = contents;

            bool hasNativeTools = request.ToolProtocolMode == AIToolProtocolMode.NativeToolCalling &&
                request.ToolChoice != AIToolChoice.None &&
                request.Tools != null &&
                request.Tools.Count > 0;
            if (hasNativeTools)
            {
                var declarations = new JArray();
                foreach (var tool in request.Tools)
                {
                    declarations.Add(new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description ?? string.Empty,
                        ["parameters"] = ConvertSchemaForGemini(tool.Parameters)
                    });
                }
                payload["tools"] = new JArray
                {
                    new JObject { ["functionDeclarations"] = declarations }
                };
                payload["toolConfig"] = new JObject
                {
                    ["functionCallingConfig"] = new JObject
                    {
                        ["mode"] = request.ToolChoice == AIToolChoice.Required ? "ANY" : "AUTO"
                    }
                };
            }

            var generationConfig = new JObject();
            if (request.MaxTokens.HasValue) generationConfig["maxOutputTokens"] = Math.Max(1, request.MaxTokens.Value);
            if (request.Temperature.HasValue) generationConfig["temperature"] = request.Temperature.Value;
            if (generationConfig.Count > 0) payload["generationConfig"] = generationConfig;
            return payload;
        }

        private static JObject ConvertMessage(AIMessage message)
        {
            string role = (message.Role ?? "user").ToLowerInvariant();
            if (role == "tool")
            {
                return new JObject
                {
                    ["role"] = "user",
                    ["parts"] = new JArray
                    {
                        new JObject
                        {
                            ["functionResponse"] = new JObject
                            {
                                ["name"] = string.IsNullOrWhiteSpace(message.ToolName) ? message.ToolCallId : message.ToolName,
                                ["response"] = new JObject
                                {
                                    ["name"] = string.IsNullOrWhiteSpace(message.ToolName) ? message.ToolCallId : message.ToolName,
                                    ["content"] = message.Content ?? string.Empty
                                }
                            }
                        }
                    }
                };
            }

            var parts = new JArray();
            if (role == "assistant" && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    parts.Add(new JObject { ["text"] = message.Content });
                }
                foreach (var call in message.ToolCalls)
                {
                    if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                    parts.Add(new JObject
                    {
                        ["functionCall"] = new JObject
                        {
                            ["name"] = call.Name,
                            ["args"] = AIProviderJson.CloneObject(call.Arguments)
                        }
                    });
                }
                return new JObject { ["role"] = "model", ["parts"] = parts };
            }

            if (message.Parts != null && message.Parts.Count > 0)
            {
                foreach (var part in message.Parts)
                {
                    if (part == null) continue;
                    if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new JObject
                        {
                            ["inline_data"] = new JObject
                            {
                                ["mime_type"] = part.MimeType,
                                ["data"] = part.Base64Data
                            }
                        });
                    }
                    else
                    {
                        parts.Add(new JObject { ["text"] = part.Text ?? string.Empty });
                    }
                }
            }
            else
            {
                parts.Add(new JObject { ["text"] = message.Content ?? string.Empty });
            }

            return new JObject
            {
                ["role"] = role == "assistant" ? "model" : "user",
                ["parts"] = parts
            };
        }

        private static JObject ConvertSchemaForGemini(JObject schema)
        {
            if (schema == null) return new JObject { ["type"] = "object", ["properties"] = new JObject() };
            var copy = AIProviderJson.CloneObject(schema);
            NormalizeSchema(copy);
            return copy;
        }

        private static void NormalizeSchema(JObject schema)
        {
            if (schema == null) return;
            var typeToken = schema["type"];
            if (typeToken is JArray typeArray)
            {
                foreach (var item in typeArray)
                {
                    string candidate = item.Value<string>();
                    if (!string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        schema["type"] = candidate;
                        break;
                    }
                }
            }
            var properties = schema["properties"] as JObject;
            if (properties != null)
            {
                foreach (var property in properties.Properties())
                {
                    NormalizeSchema(property.Value as JObject);
                }
            }
            var items = schema["items"] as JObject;
            if (items != null) NormalizeSchema(items);
            schema.Remove("additionalProperties");
            schema.Remove("strict");
        }

        private static AIProviderResponse ParseGenerateContent(string json)
        {
            var root = AIProviderJson.ParseObject(json);
            var response = new AIProviderResponse { RawJson = json, ToolCalls = new List<AIToolCall>() };
            AppendCandidate(root["candidates"]?[0] as JObject, response, null);
            response.Usage = root["usageMetadata"] as JObject;
            return response;
        }

        private static void ParseStreamChunk(string data, GeminiStreamAccumulator accumulator, Action<AIStreamEvent> onEvent)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            JObject root;
            try { root = JObject.Parse(data); }
            catch { return; }
            accumulator.RawChunks.AppendLine(data);
            var response = new AIProviderResponse { ToolCalls = new List<AIToolCall>() };
            AppendCandidate(root["candidates"]?[0] as JObject, response, onEvent);
            if (!string.IsNullOrEmpty(response.Content)) accumulator.Content.Append(response.Content);
            foreach (var call in response.ToolCalls)
            {
                accumulator.ToolCalls.Add(call);
            }
        }

        private static void AppendCandidate(JObject candidate, AIProviderResponse response, Action<AIStreamEvent> onEvent)
        {
            var parts = candidate?["content"]?["parts"] as JArray;
            if (parts == null) return;
            var text = new StringBuilder();
            foreach (var partToken in parts)
            {
                var part = partToken as JObject;
                if (part == null) continue;
                string partText = part.Value<string>("text");
                if (!string.IsNullOrEmpty(partText))
                {
                    text.Append(partText);
                    onEvent?.Invoke(new AIStreamEvent { TextDelta = partText });
                }
                var fn = part["functionCall"] as JObject;
                if (fn != null)
                {
                    string name = fn.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        response.ToolCalls.Add(AIToolCall.Create(null, name, AIProviderJson.AsObject(fn["args"])));
                    }
                }
            }
            response.Content = (response.Content ?? string.Empty) + text;
        }

        private sealed class GeminiStreamAccumulator
        {
            public readonly StringBuilder Content = new StringBuilder();
            public readonly StringBuilder RawChunks = new StringBuilder();
            public readonly List<AIToolCall> ToolCalls = new List<AIToolCall>();

            public AIProviderResponse ToResponse()
            {
                return new AIProviderResponse
                {
                    Content = Content.ToString(),
                    RawJson = RawChunks.ToString(),
                    ToolCalls = ToolCalls
                };
            }
        }
    }
}
