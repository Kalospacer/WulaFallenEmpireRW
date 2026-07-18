using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// VLM visual analysis tool.
    /// </summary>
    public class Tool_AnalyzeScreen : AITool
    {
        public override string Name => "analyze_screen";

        public override string Description =>
            "Analyze the current game screen screenshot. Provide an instruction to guide the analysis.";

        public override string UsageSchema => "{\"instruction\":\"Describe the current screen\"}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["instruction"] = SchemaString("Instruction for image analysis.", nullable: true),
                ["context"] = SchemaString("Alias for instruction.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("instruction", "context"));
        }

        private const string BaseVisionSystemPrompt = "You are a seasoned RimWorld assistant. Analyze the screenshot per instruction. Keep replies concise. Do not output tool call JSON unless explicitly asked.";

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ExecuteInternalAsync(args, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return "Vision analysis cancelled or timed out.";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Execute error: {ex}");
                return $"Vision analysis error: {ex.Message}";
            }
        }

        private async Task<string> ExecuteInternalAsync(string jsonContent, CancellationToken cancellationToken)
        {
            var argsDict = ParseJsonArgs(jsonContent);
            string instruction = TryGetString(argsDict, "instruction", out var inst) ? inst :
                               (TryGetString(argsDict, "context", out var ctx) ? ctx : "Describe the current screen, focusing on UI state and key entities.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Check VLM settings
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    return "Mod settings not initialized.";
                }

                string vlmApiKey = GetConfiguredApiKey(settings);
                if (string.IsNullOrEmpty(vlmApiKey))
                {
                    return "API key not configured. Please configure it in Mod settings.";
                }

                string base64Image = ScreenCaptureUtility.CaptureScreenAsBase64();
                if (string.IsNullOrEmpty(base64Image))
                {
                    return "Screenshot capture failed; cannot analyze screen.";
                }

                var provider = AIProviderFactory.Create(settings);
                var request = new AIProviderRequest
                {
                    RequestId = "wulaai_vision_" + Guid.NewGuid().ToString("N"),
                    SystemPrompt = BaseVisionSystemPrompt,
                    Messages = new List<AIMessage>
                    {
                        AIMessage.UserParts(new List<AIContentPart>
                        {
                            AIContentPart.TextPart(instruction),
                            AIContentPart.ImagePart("image/jpeg", base64Image)
                        })
                    },
                    MaxTokens = 512,
                    Temperature = 0.2f,
                    ToolChoice = AIToolChoice.None,
                    Stream = false,
                    TimeoutSeconds = Math.Max(2, Math.Min(600, settings.aiRequestTimeoutSeconds)),
                    LogRawTraffic = settings.logRawAiTraffic
                };
                var response = await provider.SendAsync(request, cancellationToken);
                string result = response?.Content;

                if (string.IsNullOrEmpty(result))
                {
                    return "Vision analysis produced no response. Check API settings.";
                }

                return $"Screen analysis result: {result.Trim()}";
            }
            catch (OperationCanceledException)
            {
                return "Vision analysis cancelled or timed out.";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Error: {ex}");
                return $"Vision analysis error: {ex.Message}";
            }
        }

        private static string GetConfiguredApiKey(WulaFallenEmpireSettings settings)
        {
            switch (AIProviderFactory.ParseProviderType(settings.aiProviderType))
            {
                case AIProviderType.AnthropicMessages:
                    return settings.anthropicApiKey;
                case AIProviderType.Gemini:
                    return settings.geminiApiKey;
                default:
                    return settings.apiKey;
            }
        }
    }
}
