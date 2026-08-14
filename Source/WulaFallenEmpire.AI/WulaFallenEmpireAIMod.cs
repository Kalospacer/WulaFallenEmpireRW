using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI
{
    [StaticConstructorOnStartup]
    public class WulaFallenEmpireAIMod : Mod
    {
        public static WulaAISettings settings;
        public static bool _showApiKey = false;

        private string _maxContextTokensBuffer;
        private string _maxToolStepsBuffer;
        private string _aiRequestTimeoutSecondsBuffer;
        private string _streamIdleTimeoutBuffer;
        private bool _mcpTestRunning;
        private string _mcpTestResult;

        public WulaFallenEmpireAIMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WulaAISettings>();

            // AI 程序集自己的 Harmony 实例，patch 本程序集（Patch_LetterStack 等）
            var harmony = new Harmony("tourswen.wulafallenempire.ai");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // 订阅主程序集钩子：运输舱发往舰队后触发 AI 弹幕
            WulaEventHooks.TransportPodsSentToFleet += AIAutoCommentary.ProcessEvent;

            WulaLog.Debug("[WulaFallenEmpire.AI] Harmony patches applied.");
        }

        private Vector2 _scrollPosition = Vector2.zero;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Prepare Scroll View
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, 1700f); // Adjust if more height is needed
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);

            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(viewRect);

            listingStandard.Label("Wula_AISettings_Title".Translate());

            var aiCore = Find.World?.GetComponent<AIIntelligenceCore>();
            if (aiCore != null)
            {
                bool saveAIEnabled = aiCore.IsAIEnabled;
                listingStandard.CheckboxLabeled(
                    "Wula_AISettings_SaveAIEnabled".Translate(),
                    ref saveAIEnabled,
                    "Wula_AISettings_SaveAIEnabledDesc".Translate());
                if (saveAIEnabled != aiCore.IsAIEnabled)
                {
                    aiCore.SetAIEnabled(saveAIEnabled);
                }
            }
            else
            {
                listingStandard.Label("Wula_AISettings_SaveAIUnavailable".Translate());
            }

            listingStandard.GapLine();

            listingStandard.Label("<color=cyan>AI Provider</color>");
            if (listingStandard.RadioButton("OpenAI /chat/completions", settings.aiProviderType == "OpenAIChat")) settings.aiProviderType = "OpenAIChat";
            if (listingStandard.RadioButton("Anthropic /v1/messages", settings.aiProviderType == "AnthropicMessages")) settings.aiProviderType = "AnthropicMessages";
            if (listingStandard.RadioButton("Google Gemini generateContent", settings.aiProviderType == "Gemini")) settings.aiProviderType = "Gemini";
            listingStandard.CheckboxLabeled("启用 Streaming", ref settings.enableStreaming, "开启后 provider 使用流式响应；工具调用阶段会先缓冲，最终回复可流式显示。");
            // 根据当前选中的协议，动态绑定输入字段
            if (settings.aiProviderType == "Gemini")
            {
                listingStandard.Label("<color=orange>Gemini 设置 (独立存储)</color>");

                listingStandard.Label("Gemini API Key:");
                Rect keyRect = listingStandard.GetRect(30f);
                float tw = 60f;
                Rect pRect = new Rect(keyRect.x, keyRect.y, keyRect.width - tw - 5f, keyRect.height);
                Rect tRect = new Rect(keyRect.xMax - tw, keyRect.y, tw, keyRect.height);
                if (_showApiKey) settings.geminiApiKey = Widgets.TextField(pRect, settings.geminiApiKey);
                else settings.geminiApiKey = GUI.PasswordField(pRect, settings.geminiApiKey, '•');
                Widgets.CheckboxLabeled(tRect, "Show", ref _showApiKey);

                listingStandard.Label("API 代理地址 (可选，留空则用官方 Google 节点):");
                settings.geminiBaseUrl = listingStandard.TextEntry(settings.geminiBaseUrl);

                listingStandard.Label("模型名称:");
                settings.geminiModel = listingStandard.TextEntry(settings.geminiModel);
            }
            else if (settings.aiProviderType == "AnthropicMessages")
            {
                listingStandard.Label("<color=orange>Anthropic 设置 (独立存储)</color>");

                listingStandard.Label("Anthropic API Key:");
                Rect keyRect = listingStandard.GetRect(30f);
                float tw = 60f;
                Rect pRect = new Rect(keyRect.x, keyRect.y, keyRect.width - tw - 5f, keyRect.height);
                Rect tRect = new Rect(keyRect.xMax - tw, keyRect.y, tw, keyRect.height);
                if (_showApiKey) settings.anthropicApiKey = Widgets.TextField(pRect, settings.anthropicApiKey);
                else settings.anthropicApiKey = GUI.PasswordField(pRect, settings.anthropicApiKey, '•');
                Widgets.CheckboxLabeled(tRect, "Show", ref _showApiKey);

                listingStandard.Label("Base URL:");
                settings.anthropicBaseUrl = listingStandard.TextEntry(settings.anthropicBaseUrl);

                listingStandard.Label("模型名称:");
                settings.anthropicModel = listingStandard.TextEntry(settings.anthropicModel);
            }
            else
            {
                listingStandard.Label("<color=orange>OpenAI 兼容设置 (独立存储)</color>");

                listingStandard.Label("API Key:");
                Rect keyRect = listingStandard.GetRect(30f);
                float tw = 60f;
                Rect pRect = new Rect(keyRect.x, keyRect.y, keyRect.width - tw - 5f, keyRect.height);
                Rect tRect = new Rect(keyRect.xMax - tw, keyRect.y, tw, keyRect.height);
                if (_showApiKey) settings.apiKey = Widgets.TextField(pRect, settings.apiKey);
                else settings.apiKey = GUI.PasswordField(pRect, settings.apiKey, '•');
                Widgets.CheckboxLabeled(tRect, "Show", ref _showApiKey);

                listingStandard.Label("Base URL:");
                settings.baseUrl = listingStandard.TextEntry(settings.baseUrl);

                listingStandard.Label("模型名称:");
                settings.model = listingStandard.TextEntry(settings.model);
            }

            listingStandard.GapLine();
            listingStandard.Label("Wula_AISettings_MaxContextTokens".Translate());
            listingStandard.Label("Wula_AISettings_MaxContextTokensDesc".Translate());
            Rect tokensRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(tokensRect, ref settings.maxContextTokens, ref _maxContextTokensBuffer, 1000, 1000000);

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("记录 AI raw request/response", ref settings.logRawAiTraffic, "开启后会在 RimWorld 日志中记录脱敏后的 HTTP 请求、payload、响应 body 和 SSE 累积内容。");
            listingStandard.Label("AI Request Timeout Seconds (2-600):");
            Rect timeoutRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(timeoutRect, ref settings.aiRequestTimeoutSeconds, ref _aiRequestTimeoutSecondsBuffer, 2, 600);
            listingStandard.Label("Stream Idle Timeout Seconds (5-300, 流式无数据多少秒判定中断):");
            Rect idleRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(idleRect, ref settings.streamIdleTimeoutSeconds, ref _streamIdleTimeoutBuffer, 5, 300);

            listingStandard.GapLine();
            listingStandard.Label("<color=cyan>Tool Loop Settings</color>");
            listingStandard.Label("Max Tool Steps:");
            Rect maxStepsRect = listingStandard.GetRect(Text.LineHeight);
            // 不设硬上限，玩家自己承担长 loop 的后果（配合单次超时和停止按钮兜底）
            Widgets.TextFieldNumeric(maxStepsRect, ref settings.maxToolSteps, ref _maxToolStepsBuffer, 1f);

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("显示ReAct思考折叠框", ref settings.showReactTraceInUI, "在对话窗口中显示思考/工具调用折叠面板。");

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("Wula_AISettings_AutoCommentary".Translate(), ref settings.enableAIAutoCommentary, "Wula_AISettings_AutoCommentaryDesc".Translate());
            if (settings.enableAIAutoCommentary)
            {
                listingStandard.Label("Wula_AISettings_CommentaryChance".Translate() + $" ({settings.aiCommentaryChance:P0})");
                listingStandard.Label("Wula_AISettings_CommentaryChanceDesc".Translate());
                settings.aiCommentaryChance = listingStandard.Slider(settings.aiCommentaryChance, 0f, 1f);
                settings.aiCommentaryChance = Mathf.Clamp01(settings.aiCommentaryChance);
                listingStandard.CheckboxLabeled("Wula_AISettings_NegativeOnly".Translate(), ref settings.commentOnNegativeOnly, "Wula_AISettings_NegativeOnlyDesc".Translate());
            }

            // 视觉设置部分
            listingStandard.GapLine();
            listingStandard.Label("<color=cyan>视觉与多模态设置</color>");

            listingStandard.CheckboxLabeled("是否多模态模型", ref settings.isMultimodalModel, "勾选后 AI 才会获得截图/视觉工具（take_screenshot、analyze_screen），仅当模型确实支持图片输入时勾选");

            listingStandard.GapLine();
            listingStandard.Label("<color=cyan>MCP 外部工具</color>");
            listingStandard.Label("MCP 服务器配置 JSON（形状 { \"servers\": [...] }）：");
            Rect mcpRect = listingStandard.GetRect(150f);
            settings.mcpServersJson = Widgets.TextArea(mcpRect, settings.mcpServersJson);

            Rect testRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(testRect, _mcpTestRunning ? "连接测试中..." : "连接测试"))
            {
                RunMcpConnectionTest();
            }
            if (!string.IsNullOrEmpty(_mcpTestResult))
            {
                listingStandard.Label(_mcpTestResult);
            }

            listingStandard.Label("Skill 扫描目录（可选，留空则用 mod 的 Skills/ 目录）：");
            settings.skillsDirectory = listingStandard.TextEntry(settings.skillsDirectory);

            listingStandard.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Wula Fallen Empire AI";
        }

        private async void RunMcpConnectionTest()
        {
            if (_mcpTestRunning) return;
            _mcpTestRunning = true;
            _mcpTestResult = null;
            try
            {
                string result = await McpConnectionManager.Instance.TestConnectionsAsync();
                _mcpTestResult = result;
            }
            catch (Exception ex)
            {
                _mcpTestResult = "连接测试异常: " + ex.Message;
            }
            finally
            {
                _mcpTestRunning = false;
            }
        }
    }
}
