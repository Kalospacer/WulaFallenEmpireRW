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
        private string _maxContextTokensBuffer;

        public WulaFallenEmpireMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WulaFallenEmpireSettings>();

            // 初始化Harmony
            var harmony = new Harmony("tourswen.wulafallenempire"); // 替换为您的唯一Mod ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            WulaLog.Debug("[WulaFallenEmpire] Harmony patches applied.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            
            listingStandard.Label("Wula_AISettings_Title".Translate());
            
            listingStandard.Label("Wula_AISettings_ApiKey".Translate());
            Rect apiKeyRect = listingStandard.GetRect(30f);
            // 这里我们手动实现一个带切换功能的密码输入框
            float toggleWidth = 60f;
            Rect passwordRect = new Rect(apiKeyRect.x, apiKeyRect.y, apiKeyRect.width - toggleWidth - 5f, apiKeyRect.height);
            Rect toggleRect = new Rect(apiKeyRect.xMax - toggleWidth, apiKeyRect.y, toggleWidth, apiKeyRect.height);
            
            // 使用静态布尔值或类成员来记住显示状态
            if (WulaFallenEmpireMod._showApiKey)
            {
                settings.apiKey = Widgets.TextField(passwordRect, settings.apiKey);
            }
            else
            {
                settings.apiKey = GUI.PasswordField(passwordRect, settings.apiKey, '•');
            }
            
            Widgets.CheckboxLabeled(toggleRect, "Show", ref WulaFallenEmpireMod._showApiKey);
            listingStandard.Gap(listingStandard.verticalSpacing);
            
            listingStandard.Label("Wula_AISettings_BaseUrl".Translate());
            settings.baseUrl = listingStandard.TextEntry(settings.baseUrl);
            
            listingStandard.Label("Wula_AISettings_Model".Translate());
            settings.model = listingStandard.TextEntry(settings.model);

            listingStandard.GapLine();
            listingStandard.Label("Wula_AISettings_MaxContextTokens".Translate());
            listingStandard.Label("Wula_AISettings_MaxContextTokensDesc".Translate());
            Rect tokensRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.TextFieldNumeric(tokensRect, ref settings.maxContextTokens, ref _maxContextTokensBuffer, 1000, 200000);

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("Wula_EnableDebugLogs".Translate(), ref settings.enableDebugLogs, "Wula_EnableDebugLogsDesc".Translate());

            listingStandard.GapLine();
            listingStandard.Label("Translation tools");
            Rect exportRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(exportRect, "Export DefInjected template (CN source)"))
            {
                DefInjectedExportUtility.ExportDefInjectedTemplateFromDefs(Content);
            }

            listingStandard.End();
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
