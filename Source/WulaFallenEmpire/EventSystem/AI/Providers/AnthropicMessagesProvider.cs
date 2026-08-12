using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class AnthropicMessagesProvider : IAIProvider
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public AnthropicMessagesProvider(string apiKey, string baseUrl, string model)
        {
            _apiKey = apiKey;
            _baseUrl = AIProviderJson.NormalizeBaseUrl(baseUrl, "https://api.anthropic.com");
            _model = model;
        }

        public async Task<AIProviderResponse> SendAsync(AIProviderRequest request, CancellationToken cancellationToken)
        {
            string json = await PostAsync(request, false, cancellationToken);
            var response = ParseMessage(json);
            AIProviderJson.LogStage("Anthropic", request, $"non-stream parsed contentChars={response.Content?.Length ?? 0} toolCalls={response.ToolCalls?.Count ?? 0}");
            AIProviderJson.LogUsage("Anthropic", request, response);
            return response;
        }

        public async Task<AIProviderResponse> StreamAsync(AIProviderRequest request, Action<AIStreamEvent> onEvent, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request, true);
            var watch = AIProviderJson.StartRequest("Anthropic", request, "stream");
            using (var httpRequest = BuildHttpRequest(payload))
            using (var timeoutCts = AIProviderJson.CreateTimeoutToken(request, cancellationToken))
            {
                AIProviderJson.LogRawRequest("Anthropic", request, httpRequest, payload);
                try
                {
                    using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token))
                    {
                        AIProviderJson.LogStage("Anthropic", request, $"stream headers status={(int)response.StatusCode} elapsedMs={watch.ElapsedMilliseconds}");
                        string bodyIfError = null;
                        if (!response.IsSuccessStatusCode)
                        {
                            bodyIfError = await response.Content.ReadAsStringAsync();
                            AIProviderJson.LogRawResponse("Anthropic", request, (int)response.StatusCode, bodyIfError);
                            throw new Exception($"Anthropic API error {(int)response.StatusCode}: {bodyIfError}");
                        }

                        var accumulator = new AnthropicStreamAccumulator();
                        int sseCount = await AIProviderJson.ReadSseAsync(response, (eventName, data) =>
                        {
                            ParseStreamEvent(data, accumulator, onEvent);
                        }, timeoutCts.Token);
                        onEvent?.Invoke(new AIStreamEvent { Completed = true });
                        var result = accumulator.ToResponse();
                        AIProviderJson.LogStage("Anthropic", request, $"stream done sseDataLines={sseCount} contentChars={result.Content?.Length ?? 0} toolCalls={result.ToolCalls?.Count ?? 0} elapsedMs={watch.ElapsedMilliseconds}");
                        AIProviderJson.LogUsage("Anthropic", request, result);
                        AIProviderJson.LogRawResponse("Anthropic", request, (int)response.StatusCode, result.RawJson);
                        return result;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    AIProviderJson.LogStage("Anthropic", request, $"stream {AIProviderJson.DescribeCancellation(request, timeoutCts.Token)} elapsedMs={watch.ElapsedMilliseconds}");
                    throw;
                }
            }
        }

        private async Task<string> PostAsync(AIProviderRequest request, bool stream, CancellationToken cancellationToken)
        {
            var payload = BuildPayload(request, stream);
            var watch = AIProviderJson.StartRequest("Anthropic", request, stream ? "stream" : "non-stream");
            using (var httpRequest = BuildHttpRequest(payload))
            using (var timeoutCts = AIProviderJson.CreateTimeoutToken(request, cancellationToken))
            {
                AIProviderJson.LogRawRequest("Anthropic", request, httpRequest, payload);
                try
                {
                    using (var response = await AIProviderJson.HttpClient.SendAsync(httpRequest, timeoutCts.Token))
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        AIProviderJson.LogStage("Anthropic", request, $"non-stream status={(int)response.StatusCode} bodyChars={body?.Length ?? 0} elapsedMs={watch.ElapsedMilliseconds}");
                        AIProviderJson.LogRawResponse("Anthropic", request, (int)response.StatusCode, body);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new Exception($"Anthropic API error {(int)response.StatusCode}: {body}");
                        }
                        return body;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    AIProviderJson.LogStage("Anthropic", request, $"non-stream {AIProviderJson.DescribeCancellation(request, timeoutCts.Token)} elapsedMs={watch.ElapsedMilliseconds}");
                    throw;
                }
            }
        }

        private HttpRequestMessage BuildHttpRequest(JObject payload)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/messages");
            request.Content = AIProviderJson.JsonContent(payload);
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
            }
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return request;
        }

        private JObject BuildPayload(AIProviderRequest request, bool stream)
        {
            string systemPrompt = request.SystemPrompt ?? string.Empty;
            var messages = new JArray();
            foreach (var message in request.Messages ?? new List<AIMessage>())
            {
                if (message == null) continue;
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        systemPrompt += "\n" + message.Content;
                    }
                    continue;
                }
                var converted = ConvertMessage(message);
                if (converted != null) messages.Add(converted);
            }

            var payload = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model,
                ["max_tokens"] = Math.Max(1, request.MaxTokens ?? 2048),
                ["stream"] = stream,
                ["messages"] = messages
            };
            if (!string.IsNullOrWhiteSpace(systemPrompt)) payload["system"] = systemPrompt.Trim();
            if (request.Temperature.HasValue) payload["temperature"] = request.Temperature.Value;

            bool hasNativeTools = request.ToolChoice != AIToolChoice.None &&
                request.Tools != null &&
                request.Tools.Count > 0;
            if (hasNativeTools)
            {
                var tools = new JArray();
                foreach (var tool in request.Tools)
                {
                    tools.Add(new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description ?? string.Empty,
                        ["input_schema"] = AIProviderJson.CloneObject(tool.Parameters)
                    });
                }
                payload["tools"] = tools;
                payload["tool_choice"] = ToolChoiceToAnthropic(request.ToolChoice);
            }

            return payload;
        }

        private static JObject ToolChoiceToAnthropic(AIToolChoice choice)
        {
            switch (choice)
            {
                case AIToolChoice.Required:
                    return new JObject { ["type"] = "any" };
                case AIToolChoice.None:
                    return new JObject { ["type"] = "none" };
                default:
                    return new JObject { ["type"] = "auto" };
            }
        }

        private static JObject ConvertMessage(AIMessage message)
        {
            string role = (message.Role ?? "user").ToLowerInvariant();
            if (role == "tool")
            {
                // tool_result.content accepts a block array, so a screenshot travels inside the tool
                // result itself. Emitting it as a separate user message would put two consecutive user
                // turns in the payload, which the Messages API rejects.
                JToken resultContent;
                if (message.Parts != null && message.Parts.Count > 0)
                {
                    var blocks = new JArray();
                    foreach (var part in message.Parts)
                    {
                        if (part == null) continue;
                        if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase))
                        {
                            blocks.Add(new JObject
                            {
                                ["type"] = "image",
                                ["source"] = new JObject
                                {
                                    ["type"] = "base64",
                                    ["media_type"] = part.MimeType,
                                    ["data"] = part.Base64Data
                                }
                            });
                        }
                        else
                        {
                            blocks.Add(new JObject { ["type"] = "text", ["text"] = part.Text ?? string.Empty });
                        }
                    }
                    resultContent = blocks;
                }
                else
                {
                    resultContent = message.Content ?? string.Empty;
                }

                return new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = message.ToolCallId ?? string.Empty,
                            ["content"] = resultContent
                        }
                    }
                };
            }

            var obj = new JObject { ["role"] = role == "assistant" ? "assistant" : "user" };
            if (role == "assistant" && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                var content = new JArray();
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    content.Add(new JObject { ["type"] = "text", ["text"] = message.Content });
                }
                foreach (var call in message.ToolCalls)
                {
                    if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                    content.Add(new JObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = AIProviderJson.CloneObject(call.Arguments)
                    });
                }
                obj["content"] = content;
                return obj;
            }

            if (message.Parts != null && message.Parts.Count > 0)
            {
                var content = new JArray();
                foreach (var part in message.Parts)
                {
                    if (part == null) continue;
                    if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase))
                    {
                        content.Add(new JObject
                        {
                            ["type"] = "image",
                            ["source"] = new JObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = part.MimeType,
                                ["data"] = part.Base64Data
                            }
                        });
                    }
                    else
                    {
                        content.Add(new JObject { ["type"] = "text", ["text"] = part.Text ?? string.Empty });
                    }
                }
                obj["content"] = content;
            }
            else
            {
                obj["content"] = message.Content ?? string.Empty;
            }
            return obj;
        }

        private static AIProviderResponse ParseMessage(string json)
        {
            var root = AIProviderJson.ParseObject(json);
            var response = new AIProviderResponse { RawJson = json, ToolCalls = new List<AIToolCall>() };
            var text = new StringBuilder();
            var reasoning = new StringBuilder();
            var content = root["content"] as JArray;
            if (content != null)
            {
                foreach (var item in content)
                {
                    var block = item as JObject;
                    string type = block?.Value<string>("type");
                    if (type == "text")
                    {
                        text.Append(block.Value<string>("text"));
                    }
                    else if (type == "thinking")
                    {
                        reasoning.Append(block.Value<string>("thinking"));
                    }
                    else if (type == "tool_use")
                    {
                        response.ToolCalls.Add(AIToolCall.Create(
                            block.Value<string>("id"),
                            block.Value<string>("name"),
                            AIProviderJson.AsObject(block["input"])));
                    }
                }
            }
            response.Content = text.ToString();
            response.Reasoning = reasoning.ToString();
            response.Usage = root["usage"] as JObject;
            return response;
        }

        private static void ParseStreamEvent(string data, AnthropicStreamAccumulator accumulator, Action<AIStreamEvent> onEvent)
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
            string type = root.Value<string>("type");
            int index = root.Value<int?>("index") ?? -1;
            if (type == "content_block_start")
            {
                var block = root["content_block"] as JObject;
                if (block == null || index < 0) return;
                string blockType = block.Value<string>("type");
                if (blockType == "tool_use")
                {
                    var tool = accumulator.GetTool(index);
                    tool.Id = block.Value<string>("id");
                    tool.Name = block.Value<string>("name");
                    if (block["input"] != null) tool.InputJson.Append(AIProviderJson.Compact(block["input"]));
                }
                return;
            }
            if (type != "content_block_delta") return;
            var delta = root["delta"] as JObject;
            string deltaType = delta?.Value<string>("type");
            if (deltaType == "text_delta")
            {
                string text = delta.Value<string>("text");
                if (!string.IsNullOrEmpty(text))
                {
                    accumulator.Content.Append(text);
                    onEvent?.Invoke(new AIStreamEvent { TextDelta = text });
                }
            }
            else if (deltaType == "thinking_delta")
            {
                string thinking = delta.Value<string>("thinking");
                if (!string.IsNullOrEmpty(thinking))
                {
                    accumulator.Reasoning.Append(thinking);
                    onEvent?.Invoke(new AIStreamEvent { ReasoningDelta = thinking });
                }
            }
            else if (deltaType == "input_json_delta" && index >= 0)
            {
                string partialJson = delta.Value<string>("partial_json");
                if (!string.IsNullOrEmpty(partialJson))
                {
                    accumulator.GetTool(index).InputJson.Append(partialJson);
                }
            }
        }

        private sealed class AnthropicStreamAccumulator
        {
            public readonly StringBuilder Content = new StringBuilder();
            public readonly StringBuilder Reasoning = new StringBuilder();
            public readonly StringBuilder RawChunks = new StringBuilder();
            public JObject Usage;
            private readonly Dictionary<int, ToolAccumulator> _tools = new Dictionary<int, ToolAccumulator>();

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
                    response.ToolCalls.Add(AIToolCall.Create(tool.Id, tool.Name, AIProviderJson.ParseMaybeObject(tool.InputJson.ToString())));
                }
                return response;
            }
        }

        private sealed class ToolAccumulator
        {
            public string Id;
            public string Name;
            public readonly StringBuilder InputJson = new StringBuilder();
        }
    }
}
