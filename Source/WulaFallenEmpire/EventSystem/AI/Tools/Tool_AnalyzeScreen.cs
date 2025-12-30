using System;
using System.Collections.Generic;
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

        private const string BaseVisionSystemPrompt = "You are a seasoned RimWorld assistant. Analyze the screenshot per instruction. Keep replies concise. Do not output tool call JSON unless explicitly asked.";

        public override async Task<string> ExecuteAsync(string args)
        {
            try
            {
                return await ExecuteInternalAsync(args);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Execute error: {ex}");
                return $"Vision analysis error: {ex.Message}";
            }
        }

        private async Task<string> ExecuteInternalAsync(string jsonContent)
        {
            var argsDict = ParseJsonArgs(jsonContent);
            string instruction = TryGetString(argsDict, "instruction", out var inst) ? inst :
                               (TryGetString(argsDict, "context", out var ctx) ? ctx : "Describe the current screen, focusing on UI state and key entities.");

            try
            {
                // Check VLM settings
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    return "Mod settings not initialized.";
                }

                string vlmApiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
                string vlmBaseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
                string vlmModel = settings.useGeminiProtocol ? settings.geminiModel : settings.model;

                if (string.IsNullOrEmpty(vlmApiKey))
                {
                    return "API key not configured. Please configure it in Mod settings.";
                }

                string base64Image = ScreenCaptureUtility.CaptureScreenAsBase64();
                if (string.IsNullOrEmpty(base64Image))
                {
                    return "Screenshot capture failed; cannot analyze screen.";
                }

                var client = new SimpleAIClient(vlmApiKey, vlmBaseUrl, vlmModel, settings.useGeminiProtocol);

                var messages = new List<(string role, string message)>
                {
                    ("user", instruction)
                };

                string result = await client.GetChatCompletionAsync(
                    BaseVisionSystemPrompt,
                    messages,
                    maxTokens: 512,
                    temperature: 0.2f,
                    base64Image: base64Image
                );

                if (string.IsNullOrEmpty(result))
                {
                    return "Vision analysis produced no response. Check API settings.";
                }

                return $"Screen analysis result: {result.Trim()}";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Error: {ex}");
                return $"Vision analysis error: {ex.Message}";
            }
        }
    }
}
