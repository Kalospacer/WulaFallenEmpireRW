using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string apiKey = "sk-xxxxxxxx";
        public string baseUrl = "https://api.deepseek.com";
        public string model = "deepseek-chat";
        public int maxContextTokens = 100000;
        public bool enableDebugLogs = false;
        
        // VLM (视觉语言模型) 配置
        public string vlmApiKey = "";
        public string vlmBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1";
        public string vlmModel = "qwen-vl-plus";
        public bool enableVlmFeatures = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiKey, "apiKey", "sk-xxxxxxxx");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com");
            Scribe_Values.Look(ref model, "model", "deepseek-chat");
            Scribe_Values.Look(ref maxContextTokens, "maxContextTokens", 100000);
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            
            // VLM 配置
            Scribe_Values.Look(ref vlmApiKey, "vlmApiKey", "");
            Scribe_Values.Look(ref vlmBaseUrl, "vlmBaseUrl", "https://dashscope.aliyuncs.com/compatible-mode/v1");
            Scribe_Values.Look(ref vlmModel, "vlmModel", "qwen-vl-plus");
            Scribe_Values.Look(ref enableVlmFeatures, "enableVlmFeatures", false);
            
            base.ExposeData();
        }
    }
}
