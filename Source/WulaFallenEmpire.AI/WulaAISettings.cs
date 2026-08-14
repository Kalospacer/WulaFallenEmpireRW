using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class WulaAISettings : ModSettings
    {
        public string aiProviderType = "OpenAIChat";
        public bool enableStreaming = true;
        public int maxToolSteps = 8;
        public int aiRequestTimeoutSeconds = 120;
        public int streamIdleTimeoutSeconds = 30;
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

        // 多模态能力由用户声明（模型确实支持图片输入时才勾选），勾选后 AI 才获得截图/视觉工具
        public bool isMultimodalModel = false;
        public bool enableAIAutoCommentary = false;
        public float aiCommentaryChance = 0.7f;
        public bool commentOnNegativeOnly = false;
        public string extraPersonalityPrompt = "";
        public bool showReactTraceInUI = false;

        // MCP server 列表（JSON 字符串，形状 { "servers": [...] }）。空串 = 未配置。
        public string mcpServersJson = "";
        // skill 扫描目录（SKILL.md），空串 = 用默认（mod 目录下的 Skills/）。
        public string skillsDirectory = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref aiProviderType, "aiProviderType", "OpenAIChat");
            Scribe_Values.Look(ref enableStreaming, "enableStreaming", true);
            Scribe_Values.Look(ref maxToolSteps, "maxToolSteps", 8);
            Scribe_Values.Look(ref aiRequestTimeoutSeconds, "aiRequestTimeoutSeconds", 120);
            Scribe_Values.Look(ref streamIdleTimeoutSeconds, "streamIdleTimeoutSeconds", 30);
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

            Scribe_Values.Look(ref isMultimodalModel, "isMultimodalModel", false);
            Scribe_Values.Look(ref enableAIAutoCommentary, "enableAIAutoCommentary", false);
            Scribe_Values.Look(ref aiCommentaryChance, "aiCommentaryChance", 0.7f);
            Scribe_Values.Look(ref commentOnNegativeOnly, "commentOnNegativeOnly", false);
            Scribe_Values.Look(ref extraPersonalityPrompt, "extraPersonalityPrompt", "");
            Scribe_Values.Look(ref showReactTraceInUI, "showReactTraceInUI", false);
            Scribe_Values.Look(ref mcpServersJson, "mcpServersJson", "");
            Scribe_Values.Look(ref skillsDirectory, "skillsDirectory", "");

            base.ExposeData();
        }
    }
}
