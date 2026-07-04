using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class OpenAIChatProvider : IAIProvider
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public OpenAIChatProvider(string apiKey, string baseUrl, string model)
        {
            _apiKey = apiKey;
            _baseUrl = AIProviderJson.NormalizeBaseUrl(baseUrl, "https://api.openai.com/v1");
            _model = model;
        }

        public async Task<AIProviderResponse> SendAsync(AIProviderRequest request, CancellationToken cancellationToken)
        {
            string json = await PostAsync(request, false, cancellationToken);
            var response = ParseCompletion(json);
            AIProviderJson.LogStage("OpenAI", request, $"non-stream parsed contentChars={response.Content?.Length ?? 0} toolCalls={response.ToolCalls?.Count ?? 0}");
            AIProviderJson.LogUsage("OpenAI", request, response);
            return response;
        }

        public async Task<AIProviderResponse> StreamAsync(AIProviderRequest request, Action<AIStreamEvent> onEvent, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request, true);
            var watch = AIProviderJson.StartRequest("OpenAI", request, "stream");
            using (var httpRequest = BuildHttpRequest(payload))
            using (var timeoutCts = AIProviderJson.CreateTimeoutToken(request, cancellationToken))
            {
                AIProviderJson.LogRawRequest("OpenAI", request, httpRequest, payload);
                try
                {
                    using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token))
                    {
                        AIProviderJson.LogStage("OpenAI", request, $"stream headers status={(int)response.StatusCode} elapsedMs={watch.ElapsedMilliseconds}");
                        string errorBody = null;
                        if (!response.IsSuccessStatusCode)
                        {
                            errorBody = await response.Content.ReadAsStringAsync();
                            AIProviderJson.LogRawResponse("OpenAI", request, (int)response.StatusCode, errorBody);
                            throw new Exception($"OpenAI API error {(int)response.StatusCode}: {errorBody}");
                        }

                        var accumulator = new OpenAIStreamAccumulator();
                        int sseCount = await AIProviderJson.ReadSseAsync(response, (eventName, data) =>
                        {
                            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                            {
                                onEvent?.Invoke(new AIStreamEvent { Completed = true });
                                return;
                            }
                            ParseStreamChunk(data, accumulator, onEvent);
                        }, timeoutCts.Token);
                        var result = accumulator.ToResponse();
                        AIProviderJson.LogStage("OpenAI", request, $"stream done sseDataLines={sseCount} contentChars={result.Content?.Length ?? 0} toolCalls={result.ToolCalls?.Count ?? 0} elapsedMs={watch.ElapsedMilliseconds}");
                        AIProviderJson.LogUsage("OpenAI", request, result);
                        AIProviderJson.LogRawResponse("OpenAI", request, (int)response.StatusCode, result.RawJson);
                        return result;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    AIProviderJson.LogStage("OpenAI", request, $"stream {AIProviderJson.DescribeCancellation(request, timeoutCts.Token)} elapsedMs={watch.ElapsedMilliseconds}");
                    throw;
                }
            }
        }

        private async Task<string> PostAsync(AIProviderRequest request, bool stream, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request, stream);
            var watch = AIProviderJson.StartRequest("OpenAI", request, stream ? "stream" : "non-stream");
            using (var httpRequest = BuildHttpRequest(payload))
            using (var timeoutCts = AIProviderJson.CreateTimeoutToken(request, cancellationToken))
            {
                AIProviderJson.LogRawRequest("OpenAI", request, httpRequest, payload);
                try
                {
                    using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, timeoutCts.Token))
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        AIProviderJson.LogStage("OpenAI", request, $"non-stream status={(int)response.StatusCode} bodyChars={body?.Length ?? 0} elapsedMs={watch.ElapsedMilliseconds}");
                        AIProviderJson.LogRawResponse("OpenAI", request, (int)response.StatusCode, body);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new Exception($"OpenAI API error {(int)response.StatusCode}: {body}");
                        }
                        return body;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    AIProviderJson.LogStage("OpenAI", request, $"non-stream {AIProviderJson.DescribeCancellation(request, timeoutCts.Token)} elapsedMs={watch.ElapsedMilliseconds}");
                    throw;
                }
            }
        }

        private HttpRequestMessage BuildHttpRequest(JObject payload)
        {
            string endpoint = _baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? _baseUrl
                : (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? _baseUrl + "/chat/completions" : _baseUrl + "/v1/chat/completions");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = AIProviderJson.JsonContent(payload);
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            }
            return request;
        }

        private JObject BuildPayload(AIProviderRequest request, bool stream)
        {
            var payload = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model,
                ["stream"] = stream
            };
            if (request.MaxTokens.HasValue) payload["max_tokens"] = Math.Max(1, request.MaxTokens.Value);
            if (request.Temperature.HasValue) payload["temperature"] = request.Temperature.Value;

            var messages = new JArray();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                messages.Add(new JObject { ["role"] = "system", ["content"] = request.SystemPrompt });
            }
            foreach (var message in request.Messages ?? new List<AIMessage>())
            {
                var converted = ConvertMessage(message);
                if (converted != null) messages.Add(converted);
            }
            payload["messages"] = messages;

            bool hasNativeTools = request.ToolProtocolMode == AIToolProtocolMode.NativeToolCalling &&
                request.ToolChoice != AIToolChoice.None &&
                request.Tools != null &&
                request.Tools.Count > 0;
            if (hasNativeTools)
            {
                var tools = new JArray();
                foreach (var tool in request.Tools)
                {
                    tools.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = tool.Name,
                            ["description"] = tool.Description ?? string.Empty,
                            ["parameters"] = AIProviderJson.CloneObject(tool.Parameters)
                        }
                    });
                }
                payload["tools"] = tools;
                payload["tool_choice"] = ToolChoiceToOpenAI(request.ToolChoice);
            }

            return payload;
        }

        private static JToken ToolChoiceToOpenAI(AIToolChoice choice)
        {
            switch (choice)
            {
                case AIToolChoice.Required:
                    return "required";
                case AIToolChoice.None:
                    return "none";
                default:
                    return "auto";
            }
        }

        private static JObject ConvertMessage(AIMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Role)) return null;
            string role = message.Role.ToLowerInvariant();
            var obj = new JObject { ["role"] = role };

            if (role == "tool")
            {
                obj["tool_call_id"] = message.ToolCallId ?? string.Empty;
                obj["content"] = message.Content ?? string.Empty;
                return obj;
            }

            if (role == "assistant" && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                obj["content"] = string.IsNullOrWhiteSpace(message.Content) ? JValue.CreateNull() : message.Content;
                var calls = new JArray();
                foreach (var call in message.ToolCalls)
                {
                    if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                    calls.Add(new JObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.ArgumentsJson
                        }
                    });
                }
                obj["tool_calls"] = calls;
                return obj;
            }

            if (message.Parts != null && message.Parts.Count > 0)
            {
                var parts = new JArray();
                foreach (var part in message.Parts)
                {
                    if (part == null) continue;
                    if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new JObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JObject
                            {
                                ["url"] = $"data:{part.MimeType};base64,{part.Base64Data}"
                            }
                        });
                    }
                    else
                    {
                        parts.Add(new JObject { ["type"] = "text", ["text"] = part.Text ?? string.Empty });
                    }
                }
                obj["content"] = parts;
            }
            else
            {
                obj["content"] = message.Content ?? string.Empty;
            }
            return obj;
        }

        private static AIProviderResponse ParseCompletion(string json)
        {
            var root = AIProviderJson.ParseObject(json);
            var choice = root["choices"]?[0] as JObject;
            var message = choice?["message"] as JObject;
            var result = new AIProviderResponse { RawJson = json };
            if (message == null) return result;
            result.Content = message.Value<string>("content");
            result.Reasoning = message.Value<string>("reasoning_content") ?? message.Value<string>("reasoning");
            result.ToolCalls = ParseToolCalls(message["tool_calls"] as JArray);
            result.Usage = root["usage"] as JObject;
            return result;
        }

        private static List<AIToolCall> ParseToolCalls(JArray calls)
        {
            var results = new List<AIToolCall>();
            if (calls == null) return results;
            foreach (var token in calls)
            {
                var callObj = token as JObject;
                var fn = callObj?["function"] as JObject;
                string name = fn?.Value<string>("name") ?? callObj?.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                string argsRaw = fn?["arguments"]?.Type == JTokenType.String
                    ? fn.Value<string>("arguments")
                    : AIProviderJson.Compact(fn?["arguments"] ?? callObj?["arguments"]);
                results.Add(AIToolCall.Create(callObj.Value<string>("id"), name, AIProviderJson.ParseMaybeObject(argsRaw)));
            }
            return results;
        }

        private static void ParseStreamChunk(string data, OpenAIStreamAccumulator accumulator, Action<AIStreamEvent> onEvent)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            JObject root;
            try { root = JObject.Parse(data); }
            catch { return; }

            accumulator.RawChunks.AppendLine(data);
            var usage = root["usage"] as JObject;
            if (usage != null)
            {
                accumulator.Usage = usage;
            }
            var delta = root["choices"]?[0]?["delta"] as JObject;
            if (delta == null) return;
            string text = delta.Value<string>("content");
            if (!string.IsNullOrEmpty(text))
            {
                accumulator.Content.Append(text);
                onEvent?.Invoke(new AIStreamEvent { TextDelta = text });
            }
            string reasoning = delta.Value<string>("reasoning_content");
            if (!string.IsNullOrEmpty(reasoning))
            {
                accumulator.Reasoning.Append(reasoning);
                onEvent?.Invoke(new AIStreamEvent { ReasoningDelta = reasoning });
            }
            var calls = delta["tool_calls"] as JArray;
            if (calls == null) return;
            foreach (var callToken in calls)
            {
                var call = callToken as JObject;
                if (call == null) continue;
                int index = call.Value<int?>("index") ?? accumulator.NextToolIndex;
                var acc = accumulator.GetTool(index);
                string id = call.Value<string>("id");
                if (!string.IsNullOrWhiteSpace(id)) acc.Id = id;
                var fn = call["function"] as JObject;
                if (fn == null) continue;
                string name = fn.Value<string>("name");
                if (!string.IsNullOrWhiteSpace(name)) acc.Name = name;
                string args = fn.Value<string>("arguments");
                if (!string.IsNullOrEmpty(args)) acc.Arguments.Append(args);
            }
        }

        private sealed class OpenAIStreamAccumulator
        {
            public readonly StringBuilder Content = new StringBuilder();
            public readonly StringBuilder Reasoning = new StringBuilder();
            public readonly StringBuilder RawChunks = new StringBuilder();
            public JObject Usage;
            private readonly Dictionary<int, ToolAccumulator> _tools = new Dictionary<int, ToolAccumulator>();
            public int NextToolIndex => _tools.Count;

            public ToolAccumulator GetTool(int index)
            {
                if (!_tools.TryGetValue(index, out var tool))
                {
                    tool = new ToolAccumulator();
                    _tools[index] = tool;
                }
                return tool;
            }

            public AIProviderResponse ToResponse()
            {
                var response = new AIProviderResponse
                {
                    Content = Content.ToString(),
                    Reasoning = Reasoning.ToString(),
                    RawJson = RawChunks.ToString(),
                    Usage = Usage,
                    ToolCalls = new List<AIToolCall>()
                };
                foreach (var entry in _tools)
                {
                    var tool = entry.Value;
                    if (string.IsNullOrWhiteSpace(tool.Name)) continue;
                    response.ToolCalls.Add(AIToolCall.Create(tool.Id, tool.Name, AIProviderJson.ParseMaybeObject(tool.Arguments.ToString())));
                }
                return response;
            }
        }

        private sealed class ToolAccumulator
        {
            public string Id;
            public string Name;
            public readonly StringBuilder Arguments = new StringBuilder();
        }
    }
}
