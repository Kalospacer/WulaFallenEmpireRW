using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public sealed class AIToolLoopRunner
    {
        private readonly IAIProvider _provider;
        private readonly AIToolRegistry _registry;
        private readonly AIToolRunner _toolRunner;
        private readonly string _baseSystemPrompt;
        private readonly bool _enableStreaming;
        private readonly int _maxToolSteps;
        private readonly int _requestTimeoutSeconds;
        private readonly bool _logRawTraffic;
        private readonly Action<string> _onFinalContent;
        private readonly Action<string> _onStreamingDelta;
        private readonly Action<string> _onReasoningDelta;
        private readonly Action<IReadOnlyList<AIToolCall>> _onToolCalls;
        private readonly Action<AIToolResult> _onToolResult;
        private readonly Action<string> _onTrace;
        private readonly Action<JObject> _onUsage;

        public AIToolLoopRunner(
            IAIProvider provider,
            AIToolRegistry registry,
            string baseSystemPrompt,
            bool enableStreaming,
            int maxToolSteps,
            int requestTimeoutSeconds,
            bool logRawTraffic,
            Action<string> onFinalContent,
            Action<string> onStreamingDelta,
            Action<IReadOnlyList<AIToolCall>> onToolCalls,
            Action<AIToolResult> onToolResult,
            Action<string> onTrace,
            Action<string> onReasoningDelta = null,
            TimeSpan? streamIdleTimeout = null,
            Action<JObject> onUsage = null)
        {
            _provider = provider;
            _registry = registry;
            _toolRunner = new AIToolRunner(registry);
            _baseSystemPrompt = baseSystemPrompt ?? string.Empty;
            _enableStreaming = enableStreaming;
            _maxToolSteps = Math.Max(1, maxToolSteps);
            _requestTimeoutSeconds = Math.Max(2, Math.Min(600, requestTimeoutSeconds));
            _logRawTraffic = logRawTraffic;
            _onFinalContent = onFinalContent;
            _onStreamingDelta = onStreamingDelta;
            _onReasoningDelta = onReasoningDelta;
            _onToolCalls = onToolCalls;
            _onToolResult = onToolResult;
            _onTrace = onTrace;
            _onUsage = onUsage;
            _streamIdleTimeout = streamIdleTimeout;
        }

        private readonly TimeSpan? _streamIdleTimeout;

        public async Task<AIProviderResponse> RunAsync(List<AIMessage> messages, int? maxTokens, float? temperature, CancellationToken cancellationToken)
        {
            if (_provider == null) throw new InvalidOperationException("AI provider is not configured.");
            if (messages == null) throw new ArgumentNullException(nameof(messages));

            var toolDefinitions = _registry.GetDefinitions();
            for (int step = 1; step <= _maxToolSteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _onTrace?.Invoke($"Tool loop step {step}: requesting model.");
                var request = BuildRequest(messages, toolDefinitions, maxTokens, temperature, toolsEnabled: true);
                // Stream to the UI here too: the model answers without any tool call on the very first
                // step in the common case, and that reply never reaches RequestFinalWithoutToolsAsync.
                // Passing false here meant the SSE stream was consumed and every delta thrown away.
                var response = await QueryAsync(request, allowLiveStreaming: true, cancellationToken);

                if (!response.HasToolCalls)
                {
                    _onTrace?.Invoke("No tool calls returned; using provider content as final response.");
                    CalibrateTokenEstimate(messages, response);
                    FinalizeVisibleResponse(messages, response);
                    return response;
                }

                messages.Add(AIMessage.AssistantToolCalls(
                    response.ToolCalls,
                    string.IsNullOrWhiteSpace(response.Content) ? null : response.Content,
                    string.IsNullOrWhiteSpace(response.ReasoningContent) ? null : response.ReasoningContent));
                _onToolCalls?.Invoke(response.ToolCalls);
                _onTrace?.Invoke("Tool calls: " + string.Join(", ", response.ToolCalls.Select(c => c.Name)));
                foreach (var call in response.ToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(call.Id))
                    {
                        call.Id = "call_" + Guid.NewGuid().ToString("N");
                    }
                    var result = await _toolRunner.ExecuteAsync(call, cancellationToken);
                    string content = result.Content ?? string.Empty;
                    // Multimodal output (e.g. a captured screenshot) rides along on the tool result itself
                    // rather than as a separate follow-up user message. Appending a second user-role
                    // message straight after the tool result produced two consecutive user turns, which
                    // the Anthropic Messages API rejects outright ("roles must alternate"). Each provider
                    // folds these parts into its own tool-result shape instead.
                    var imageParts = result.ContentParts?
                        .Where(p => p != null && string.Equals(p.Type, "image", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (imageParts != null && imageParts.Count > 0)
                    {
                        var resultParts = new List<AIContentPart> { AIContentPart.TextPart(content) };
                        resultParts.AddRange(imageParts);
                        messages.Add(AIMessage.ToolResultParts(call.Id, call.Name, content, resultParts));
                    }
                    else
                    {
                        messages.Add(AIMessage.ToolResult(call.Id, call.Name, content));
                    }
                    _onToolResult?.Invoke(result);
                    _onTrace?.Invoke($"Tool '{call.Name}' Result: {content}");
                }
            }

            _onTrace?.Invoke($"Tool loop reached max steps ({_maxToolSteps}); requesting final response without tools.");
            return await RequestFinalWithoutToolsAsync(messages, maxTokens, temperature, cancellationToken);
        }

        public async Task<AIProviderResponse> RunPlainAsync(List<AIMessage> messages, int? maxTokens, float? temperature, CancellationToken cancellationToken)
        {
            var request = BuildRequest(messages, new List<AIToolDefinition>(), maxTokens, temperature, toolsEnabled: false);
            var response = await QueryAsync(request, allowLiveStreaming: _enableStreaming, cancellationToken);
            FinalizeVisibleResponse(messages, response);
            return response;
        }

        private AIProviderRequest BuildRequest(List<AIMessage> messages, List<AIToolDefinition> tools, int? maxTokens, float? temperature, bool toolsEnabled)
        {
            string systemPrompt = _baseSystemPrompt;
            if (toolsEnabled)
            {
                systemPrompt += "\n\nYou may use the provided tools when you need game state or need to perform an in-game action. Do not invent tool results.";
            }
            else
            {
                systemPrompt += "\n\nTools are disabled for this turn. Reply naturally using the available context.";
            }

            return new AIProviderRequest
            {
                RequestId = "wulaai_" + Guid.NewGuid().ToString("N"),
                SystemPrompt = systemPrompt,
                Messages = messages.ToList(),
                Tools = toolsEnabled ? (tools ?? new List<AIToolDefinition>()) : new List<AIToolDefinition>(),
                MaxTokens = maxTokens,
                Temperature = temperature,
                Stream = _enableStreaming,
                TimeoutSeconds = _requestTimeoutSeconds,
                LogRawTraffic = _logRawTraffic,
                ToolChoice = toolsEnabled ? AIToolChoice.Auto : AIToolChoice.None,
                StreamIdleTimeout = _streamIdleTimeout
            };
        }

        private async Task<AIProviderResponse> QueryAsync(AIProviderRequest request, bool allowLiveStreaming, CancellationToken cancellationToken)
        {
            bool shouldStream = _enableStreaming && request.Stream;
            _onTrace?.Invoke($"Provider request {request.RequestId}: mode={(shouldStream ? "stream" : "non-stream")}, timeout={request.TimeoutSeconds}s, live={allowLiveStreaming}.");
            if (!shouldStream)
            {
                var response = await _provider.SendAsync(request, cancellationToken);
                _onTrace?.Invoke($"Provider request {request.RequestId}: completed, contentChars={response?.Content?.Length ?? 0}, toolCalls={response?.ToolCalls?.Count ?? 0}.");
                return response;
            }
            try
            {
                var response = await _provider.StreamAsync(request, evt =>
                {
                    if (evt == null) return;
                    if (!allowLiveStreaming) return;
                    if (!string.IsNullOrEmpty(evt.TextDelta))
                    {
                        _onStreamingDelta?.Invoke(evt.TextDelta);
                    }
                    if (!string.IsNullOrEmpty(evt.ReasoningDelta))
                    {
                        _onReasoningDelta?.Invoke(evt.ReasoningDelta);
                    }
                }, cancellationToken);
                _onTrace?.Invoke($"Provider request {request.RequestId}: stream completed, contentChars={response?.Content?.Length ?? 0}, toolCalls={response?.ToolCalls?.Count ?? 0}.");
                return response;
            }
            catch (OperationCanceledException)
            {
                _onTrace?.Invoke($"Provider request {request.RequestId}: cancelled or timed out.");
                throw;
            }
            catch (Exception ex)
            {
                _onTrace?.Invoke($"Streaming failed; retrying non-stream. {ex.Message}");
                request.RequestId = "wulaai_" + Guid.NewGuid().ToString("N");
                request.Stream = false;
                var response = await _provider.SendAsync(request, cancellationToken);
                _onTrace?.Invoke($"Provider request {request.RequestId}: non-stream retry completed, contentChars={response?.Content?.Length ?? 0}, toolCalls={response?.ToolCalls?.Count ?? 0}.");
                return response;
            }
        }

        private async Task<AIProviderResponse> RequestFinalWithoutToolsAsync(List<AIMessage> messages, int? maxTokens, float? temperature, CancellationToken cancellationToken)
        {
            var finalRequest = BuildRequest(messages, new List<AIToolDefinition>(), maxTokens, temperature, toolsEnabled: false);
            var finalResponse = await QueryAsync(finalRequest, allowLiveStreaming: true, cancellationToken);
            FinalizeVisibleResponse(messages, finalResponse);
            return finalResponse;
        }

        private void FinalizeVisibleResponse(List<AIMessage> messages, AIProviderResponse response)
        {
            string content = response?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                content = response?.HasToolCalls == true
                    ? string.Empty
                    : "No response.";
            }
            messages.Add(AIMessage.Assistant(content));
            _onFinalContent?.Invoke(content);
            _onUsage?.Invoke(response?.Usage);
        }

        /// <summary>
        /// Feeds the response's real usage back into the context-budget estimate (chars-per-token),
        /// so <c>CompressHistoryIfNeeded</c> triggers at the model's true tokenizer rate over time.
        /// </summary>
        private static void CalibrateTokenEstimate(List<AIMessage> messages, AIProviderResponse response)
        {
            long promptTokens = AIProviderJson.ExtractPromptTokens(response?.Usage);
            if (promptTokens <= 0) return;
            int promptChars = 0;
            if (messages != null)
            {
                foreach (var m in messages)
                {
                    promptChars += m?.Content?.Length ?? 0;
                }
            }
            AIIntelligenceCore.CalibrateCharsPerToken(promptTokens, promptChars);
        }
    }
}
