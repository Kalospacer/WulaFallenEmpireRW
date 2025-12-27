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
            "分析当前游戏屏幕截图，了解玩家正在查看什么区域或内容。需要配置 VLM API 密钥。";
        
        public override string UsageSchema => 
            "<analyze_screen><context>分析目标，如：玩家在看什么区域</context></analyze_screen>";
        
        private const string VisionSystemPrompt = @"
你是一个 RimWorld 游戏屏幕分析助手。分析截图并用简洁中文描述：
- 玩家正在查看的区域（如：殖民地基地、世界地图、菜单界面）
- 可见的重要建筑、角色、资源
- 任何明显的问题或特殊状态
保持回答简洁，不超过100字。不要使用 XML 标签。";
        
        public override string Execute(string args)
        {
            // 由于 VLM API 调用是异步的，我们需要同步等待结果
            // 这在 Unity 主线程上可能会阻塞，但工具执行通常在异步上下文中调用
            try
            {
                var task = ExecuteInternalAsync(args);
                // 使用 GetAwaiter().GetResult() 来同步等待，避免死锁
                return task.GetAwaiter().GetResult();
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
            string context = argsDict.TryGetValue("context", out var ctx) ? ctx : "描述当前屏幕内容";
            
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
                    VisionSystemPrompt,
                    context,
                    base64Image,
                    maxTokens: 256,
                    temperature: 0.3f
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
