using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string aiProviderType = "OpenAIChat";
        public string toolProtocolMode = "NativeToolCalling";
        public bool enableStreaming = true;
        public int maxToolSteps = 8;
        public int aiRequestTimeoutSeconds = 120;
        public bool logRawAiTraffic = false;

        public string apiKey = "sk-xxxxxxxx";
        public string baseUrl = "https://api.deepseek.com/v1";
        public string model = "deepseek-chat";

        public string anthropicApiKey = "";
        public string anthropicBaseUrl = "https://api.anthropic.com";
        public string anthropicModel = "claude-sonnet-4-5";
        
        // Gemini 专属配置 (独立存储)
        public string geminiApiKey = "";
        public string geminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        public string geminiModel = "gemini-2.5-flash";
        
        public int maxContextTokens = 100000;
        public bool enableDebugLogs = false;
        
        // 视觉功能配置
        public bool enableVlmFeatures = false;
        public bool enableAIAutoCommentary = false;
        public float aiCommentaryChance = 0.7f;
        public bool commentOnNegativeOnly = false;
        public string extraPersonalityPrompt = "";
        public int reactMaxSteps = 0; // Deprecated: step limit removed (unlimited).
        public int reactMaxStepsMax = 0; // Deprecated: step limit removed (unlimited).
        public float reactMaxSeconds = 60f;
        public bool showReactTraceInUI = false;
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref aiProviderType, "aiProviderType", "OpenAIChat");
            Scribe_Values.Look(ref toolProtocolMode, "toolProtocolMode", "NativeToolCalling");
            Scribe_Values.Look(ref enableStreaming, "enableStreaming", true);
            Scribe_Values.Look(ref maxToolSteps, "maxToolSteps", 8);
            Scribe_Values.Look(ref aiRequestTimeoutSeconds, "aiRequestTimeoutSeconds", 120);
            Scribe_Values.Look(ref logRawAiTraffic, "logRawAiTraffic", false);

            Scribe_Values.Look(ref apiKey, "apiKey", "sk-xxxxxxxx");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com/v1");
            Scribe_Values.Look(ref model, "model", "deepseek-chat");

            Scribe_Values.Look(ref anthropicApiKey, "anthropicApiKey", "");
            Scribe_Values.Look(ref anthropicBaseUrl, "anthropicBaseUrl", "https://api.anthropic.com");
            Scribe_Values.Look(ref anthropicModel, "anthropicModel", "claude-sonnet-4-5");
            
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref geminiBaseUrl, "geminiBaseUrl", "https://generativelanguage.googleapis.com/v1beta");
            Scribe_Values.Look(ref geminiModel, "geminiModel", "gemini-2.5-flash");
            
            Scribe_Values.Look(ref maxContextTokens, "maxContextTokens", 100000);
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            
            // 简化后的视觉配置
            Scribe_Values.Look(ref enableVlmFeatures, "enableVlmFeatures", false);
            Scribe_Values.Look(ref enableAIAutoCommentary, "enableAIAutoCommentary", false);
            Scribe_Values.Look(ref aiCommentaryChance, "aiCommentaryChance", 0.7f);
            Scribe_Values.Look(ref commentOnNegativeOnly, "commentOnNegativeOnly", false);
            Scribe_Values.Look(ref extraPersonalityPrompt, "extraPersonalityPrompt", "");
            Scribe_Values.Look(ref reactMaxSteps, "reactMaxSteps", 0);
            Scribe_Values.Look(ref reactMaxStepsMax, "reactMaxStepsMax", 0);
            Scribe_Values.Look(ref reactMaxSeconds, "reactMaxSeconds", 60f);
            Scribe_Values.Look(ref showReactTraceInUI, "showReactTraceInUI", false);
            
            base.ExposeData();
        }
    }
}
