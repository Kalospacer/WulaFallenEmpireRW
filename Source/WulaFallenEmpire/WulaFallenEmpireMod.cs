using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class WulaFallenEmpireMod : Mod
    {
        public static WulaFallenEmpireSettings settings;
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
            settings.apiKey = listingStandard.TextEntry(settings.apiKey);
            
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
            listingStandard.CheckboxLabeled("Enable Debug Logs".Translate(), ref settings.enableDebugLogs, "Enable detailed debug logging (independent of DevMode)".Translate());

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
