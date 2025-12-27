using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string apiKey = "sk-xxxxxxxx";
        public string baseUrl = "https://api.deepseek.com";
        public string model = "deepseek-chat";
        public bool useGeminiProtocol = false; // 是否使用 Google Gemini 协议格式
        public int maxContextTokens = 100000;
        public bool enableDebugLogs = false;
        
        // 视觉功能配置
        public bool enableVlmFeatures = false;
        public bool useNativeMultimodal = true; // 默认启用原生多模态
        public bool showThinkingProcess = true; // 是否显示中间思考过过程
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiKey, "apiKey", "sk-xxxxxxxx");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com");
            Scribe_Values.Look(ref model, "model", "deepseek-chat");
            Scribe_Values.Look(ref useGeminiProtocol, "useGeminiProtocol", false);
            Scribe_Values.Look(ref maxContextTokens, "maxContextTokens", 100000);
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            
            // 简化后的视觉配置
            Scribe_Values.Look(ref enableVlmFeatures, "enableVlmFeatures", false);
            Scribe_Values.Look(ref useNativeMultimodal, "useNativeMultimodal", true);
            Scribe_Values.Look(ref showThinkingProcess, "showThinkingProcess", true);
            
            base.ExposeData();
        }
    }
}
