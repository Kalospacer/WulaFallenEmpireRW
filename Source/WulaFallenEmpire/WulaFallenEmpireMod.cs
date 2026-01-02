using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using WulaFallenEmpire.Utils;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class WulaFallenEmpireMod : Mod
    {
        public static WulaFallenEmpireSettings settings;
        public static bool _showApiKey = false;
        public static bool _showVlmApiKey = false;
        private string _maxContextTokensBuffer;
        private string _reactMaxSecondsBuffer;

        public WulaFallenEmpireMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WulaFallenEmpireSettings>();

            // 初始化Harmony
            var harmony = new Harmony("tourswen.wulafallenempire"); // 替换为您的唯一Mod ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            WulaLog.Debug("[WulaFallenEmpire] Harmony patches applied.");
        }

        private Vector2 _scrollPosition = Vector2.zero;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Prepare Scroll View
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, 1000f); // Adjust 1000f if more height is needed
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);

            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(viewRect);
            
            listingStandard.Label("Wula_AISettings_Title".Translate());
            
            listingStandard.Label("<color=cyan>AI 核心协议选择</color>");
            bool currentIsGemini = settings.useGeminiProtocol;
            if (listingStandard.RadioButton("OpenAI / 常用兼容格式 (DeepSeek, ChatGPT)", !currentIsGemini)) settings.useGeminiProtocol = false;
            if (listingStandard.RadioButton("Google Gemini 原生格式 (支持本地多模态)", currentIsGemini)) settings.useGeminiProtocol = true;
            listingStandard.CheckboxLabeled("使用原生函数调用 API", ref settings.useNativeToolApi,
                "对于训练过工具调用能力的全量大模型推荐开启，如果使用较弱的小模型或者无法调用工具可以尝试关闭该选项以使用提示词兼容函数调用模式。对于Deepseek,ChatGPT,Gemini,GLM,Kimi2,Qwen模型都推荐开启，其他模型未测试。");
            listingStandard.GapLine();

            // 根据当前选中的协议，动态绑定输入字段
            if (settings.useGeminiProtocol)
            {
                listingStandard.Label("<color=orange>Gemini 设置 (独立存储)</color>");
                
                listingStandard.Label("Gemini API Key:");
                Rect keyRect = listingStandard.GetRect(30f);
                float tw = 60f;
                Rect pRect = new Rect(keyRect.x, keyRect.y, keyRect.width - tw - 5f, keyRect.height);
                Rect tRect = new Rect(keyRect.xMax - tw, keyRect.y, tw, keyRect.height);
                if (WulaFallenEmpireMod._showApiKey) settings.geminiApiKey = Widgets.TextField(pRect, settings.geminiApiKey);
                else settings.geminiApiKey = GUI.PasswordField(pRect, settings.geminiApiKey, '•');
                Widgets.CheckboxLabeled(tRect, "Show", ref WulaFallenEmpireMod._showApiKey);
                
                listingStandard.Label("API 代理地址 (可选，留空则用官方 Google 节点):");
                settings.geminiBaseUrl = listingStandard.TextEntry(settings.geminiBaseUrl);
                
                listingStandard.Label("模型名称:");
                settings.geminiModel = listingStandard.TextEntry(settings.geminiModel);
            }
            else
            {
                listingStandard.Label("<color=orange>OpenAI 兼容设置 (独立存储)</color>");
                
                listingStandard.Label("API Key:");
                Rect keyRect = listingStandard.GetRect(30f);
                float tw = 60f;
                Rect pRect = new Rect(keyRect.x, keyRect.y, keyRect.width - tw - 5f, keyRect.height);
                Rect tRect = new Rect(keyRect.xMax - tw, keyRect.y, tw, keyRect.height);
                if (WulaFallenEmpireMod._showApiKey) settings.apiKey = Widgets.TextField(pRect, settings.apiKey);
                else settings.apiKey = GUI.PasswordField(pRect, settings.apiKey, '•');
                Widgets.CheckboxLabeled(tRect, "Show", ref WulaFallenEmpireMod._showApiKey);
                
                listingStandard.Label("Base URL:");
                settings.baseUrl = listingStandard.TextEntry(settings.baseUrl);
                
                listingStandard.Label("模型名称:");
                settings.model = listingStandard.TextEntry(settings.model);
            }

            listingStandard.GapLine();
            listingStandard.Label("Wula_AISettings_MaxContextTokens".Translate());
            listingStandard.Label("Wula_AISettings_MaxContextTokensDesc".Translate());
            Rect tokensRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(tokensRect, ref settings.maxContextTokens, ref _maxContextTokensBuffer, 1000, 200000);

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("Wula_EnableDebugLogs".Translate(), ref settings.enableDebugLogs, "Wula_EnableDebugLogsDesc".Translate());

            listingStandard.GapLine();
            listingStandard.Label("<color=cyan>ReAct Loop Settings</color>");
            listingStandard.Label("Max Seconds (min 2):");
            Rect secondsRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(secondsRect, ref settings.reactMaxSeconds, ref _reactMaxSecondsBuffer, 10f, 600f);

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
            
            listingStandard.CheckboxLabeled("启用视觉交互能力", ref settings.enableVlmFeatures, "启用后 AI 可以截取屏幕并理解游戏画面");
            

            listingStandard.GapLine();
            listingStandard.Label("Translation tools");
            Rect exportRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(exportRect, "Export DefInjected template (CN source)"))
            {
                DefInjectedExportUtility.ExportDefInjectedTemplateFromDefs(Content);
            }

            listingStandard.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Wula Fallen Empire";
        }
    }

     [StaticConstructorOnStartup]
    public static class StartupLogger
    {
        static StartupLogger()
        {
            WulaLog.Debug("WulaFallenEmpire Mod DLL, version 1.0.2, has been loaded.");
        }
    }
}
