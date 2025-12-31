using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string apiKey = "sk-xxxxxxxx";
        public string baseUrl = "https://api.deepseek.com/v1";
        public string model = "deepseek-chat";
        
        // Gemini 专属配置 (独立存储)
        public string geminiApiKey = "";
        public string geminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        public string geminiModel = "gemini-2.5-flash";
        
        public bool useGeminiProtocol = false; // 是否使用 Google Gemini 协议格式
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
            Scribe_Values.Look(ref apiKey, "apiKey", "sk-xxxxxxxx");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com/v1");
            Scribe_Values.Look(ref model, "model", "deepseek-chat");
            
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref geminiBaseUrl, "geminiBaseUrl", "https://generativelanguage.googleapis.com/v1beta");
            Scribe_Values.Look(ref geminiModel, "geminiModel", "gemini-2.5-flash");
            
            Scribe_Values.Look(ref useGeminiProtocol, "useGeminiProtocol", false);
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
