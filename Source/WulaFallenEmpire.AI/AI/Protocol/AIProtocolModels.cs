using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public enum AIProviderType
    {
        OpenAIChat,
        AnthropicMessages,
        Gemini
    }

    public enum AIToolChoice
    {
        Auto,
        None,
        Required
    }

    public sealed class AIContentPart
    {
        public string Type;
        public string Text;
        public string MimeType;
        public string Base64Data;

        public static AIContentPart TextPart(string text)
        {
            return new AIContentPart { Type = "text", Text = text ?? string.Empty };
        }

        public static AIContentPart ImagePart(string mimeType, string base64Data)
        {
            return new AIContentPart
            {
                Type = "image",
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType,
                Base64Data = base64Data ?? string.Empty
            };
        }
    }

    public sealed class AIMessage
    {
        public string Role;
        public string Content;
        public List<AIContentPart> Parts;
        public List<AIToolCall> ToolCalls;
        public string ToolCallId;
        public string ToolName;
        public string ReasoningContent;

        public static AIMessage System(string content)
        {
            return new AIMessage { Role = "system", Content = content ?? string.Empty };
        }

        public static AIMessage User(string content)
        {
            return new AIMessage { Role = "user", Content = content ?? string.Empty };
        }

        public static AIMessage UserParts(List<AIContentPart> parts)
        {
            return new AIMessage { Role = "user", Parts = parts ?? new List<AIContentPart>() };
        }

        public static AIMessage Assistant(string content)
        {
            return new AIMessage { Role = "assistant", Content = content ?? string.Empty };
        }

        public static AIMessage AssistantToolCalls(List<AIToolCall> toolCalls, string content = null, string reasoningContent = null)
        {
            return new AIMessage
            {
                Role = "assistant",
                Content = content,
                ToolCalls = toolCalls ?? new List<AIToolCall>(),
                ReasoningContent = reasoningContent
            };
        }

        public static AIMessage ToolResult(string toolCallId, string toolName, string content)
        {
            return new AIMessage
            {
                Role = "tool",
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Content = content ?? string.Empty
            };
        }

        /// <summary>
        /// A tool result that also carries multimodal parts (e.g. a captured screenshot). The text
        /// <see cref="Content"/> is kept so the provider tool_result pairing and text replay still work;
        /// <see cref="Parts"/> holds the image blocks the model can actually see on the next turn.
        /// </summary>
        public static AIMessage ToolResultParts(string toolCallId, string toolName, string content, List<AIContentPart> parts)
        {
            var message = ToolResult(toolCallId, toolName, content);
            message.Parts = parts;
            return message;
        }
    }

    public sealed class AIToolCall
    {
        public string Id;
        public string Name;
        public JObject Arguments;

        public string ArgumentsJson => (Arguments ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None);

        public static AIToolCall Create(string id, string name, JObject arguments)
        {
            return new AIToolCall
            {
                Id = string.IsNullOrWhiteSpace(id) ? "call_" + Guid.NewGuid().ToString("N") : id,
                Name = name ?? string.Empty,
                Arguments = arguments ?? new JObject()
            };
        }
    }

    public sealed class AIToolResult
    {
        public string ToolCallId;
        public string ToolName;
        public string Content;
        public bool IsError;

        /// <summary>
        /// Optional multimodal parts produced by the tool (e.g. a captured screenshot as an
        /// <see cref="AIContentPart.ImagePart"/>). Null or empty means a plain-text result, which is the
        /// common case and changes nothing. These are injected as a follow-up user image message so the
        /// model can see them; they are transient and are not written to conversation history.
        /// </summary>
        public List<AIContentPart> ContentParts;
    }

    public sealed class AIToolDefinition
    {
        public string Name;
        public string Description;
        public JObject Parameters;
    }

    public sealed class AIProviderRequest
    {
        public string RequestId;
        public string SystemPrompt;
        public List<AIMessage> Messages = new List<AIMessage>();
        public List<AIToolDefinition> Tools = new List<AIToolDefinition>();
        public string Model;
        public int? MaxTokens;
        public float? Temperature;
        public bool Stream;
        public int TimeoutSeconds = 120;
        public bool LogRawTraffic;
        public AIToolChoice ToolChoice = AIToolChoice.Auto;
        /// <summary>
        /// When non-null, ask the provider for schema-constrained JSON output (codex output_schema).
        /// Providers without a native JSON mode emulate it with a forced tool call; the parsed object
        /// lands in <see cref="AIProviderResponse.StructuredOutput"/> and <see cref="Content"/> mirrors
        /// the raw JSON text.
        /// </summary>
        public JObject OutputSchema;
        /// <summary>Idle-stream watchdog: cancel the request when no SSE data arrives for this long.</summary>
        public TimeSpan? StreamIdleTimeout;
    }

    public sealed class AIProviderResponse
    {
        public string Content;
        public string Reasoning;
        public string ReasoningContent;
        public List<AIToolCall> ToolCalls = new List<AIToolCall>();
        public JObject Usage;
        public string RawJson;
        /// <summary>Parsed JSON for structured-output requests; null when no OutputSchema was set.</summary>
        public JObject StructuredOutput;

        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }

    public sealed class AIStreamEvent
    {
        public string TextDelta;
        public string ReasoningDelta;
        public bool Completed;
        public string Error;
        /// <summary>Token usage of the request that produced this event (set on the final response).</summary>
        public JObject Usage;
    }
}
