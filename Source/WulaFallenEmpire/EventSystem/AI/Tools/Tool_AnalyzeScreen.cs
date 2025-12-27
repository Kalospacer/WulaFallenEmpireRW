using System;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// VLM 视觉分析工具 - 截取游戏屏幕并使用视觉语言模型分析
    /// </summary>
    public class Tool_AnalyzeScreen : AITool
    {
        public override string Name => "analyze_screen";
        
        public override string Description => 
            "分析当前游戏屏幕截图。你可以提供具体的指令（instruction）告诉视觉模型你需要观察什么、寻找什么、或者如何描述屏幕。";
        
        public override string UsageSchema => 
            "<analyze_screen><instruction>给视觉模型的具体指令。例如：'找到科研按钮的比例坐标' 或 '描述当前角色的健康状态栏内容'</instruction></analyze_screen>";
        
        private const string BaseVisionSystemPrompt = "你是一个专业的老练 RimWorld 助手。你会根据指示分析屏幕截图。保持回答专业且简洁。不要输出 XML 标签，除非被明确要求。";
        
        public override async Task<string> ExecuteAsync(string args)
        {
            try
            {
                return await ExecuteInternalAsync(args);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Execute error: {ex}");
                return $"视觉分析出错: {ex.Message}";
            }
        }

        private async Task<string> ExecuteInternalAsync(string xmlContent)
        {
            var argsDict = ParseXmlArgs(xmlContent);
            // 优先使用 instruction，兼容旧的 context 参数
            string instruction = argsDict.TryGetValue("instruction", out var inst) ? inst : 
                               (argsDict.TryGetValue("context", out var ctx) ? ctx : "描述当前屏幕内容，重点关注 UI 状态和重要实体。");
            
            try
            {
                // 检查 VLM 配置
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    return "Mod 设置未初始化。";
                }
                
                // 使用主 API 密钥（如果没有单独配置 VLM 密钥）
                string vlmApiKey = !string.IsNullOrEmpty(settings.vlmApiKey) ? settings.vlmApiKey : settings.apiKey;
                string vlmBaseUrl = !string.IsNullOrEmpty(settings.vlmBaseUrl) ? settings.vlmBaseUrl : "https://dashscope.aliyuncs.com/compatible-mode/v1";
                string vlmModel = !string.IsNullOrEmpty(settings.vlmModel) ? settings.vlmModel : "qwen-vl-plus";
                
                if (string.IsNullOrEmpty(vlmApiKey))
                {
                    return "VLM API 密钥未配置。请在 Mod 设置中配置 API 密钥。";
                }
                
                // 截取屏幕
                string base64Image = ScreenCaptureUtility.CaptureScreenAsBase64();
                if (string.IsNullOrEmpty(base64Image))
                {
                    return "截屏失败，无法分析屏幕。";
                }
                
                // 调用 VLM API
                var client = new SimpleAIClient(vlmApiKey, vlmBaseUrl, vlmModel);
                
                string result = await client.GetVisionCompletionAsync(
                    BaseVisionSystemPrompt,
                    instruction,
                    base64Image,
                    maxTokens: 512, // 增加 token 数以支持更复杂的分析指令响应
                    temperature: 0.2f
                );
                
                if (string.IsNullOrEmpty(result))
                {
                    return "VLM 分析无响应，请检查 API 配置。";
                }
                
                return $"屏幕分析结果: {result.Trim()}";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_AnalyzeScreen] Error: {ex}");
                return $"视觉分析出错: {ex.Message}";
            }
        }
    }
}
