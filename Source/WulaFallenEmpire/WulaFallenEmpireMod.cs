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

        public WulaFallenEmpireMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WulaFallenEmpireSettings>();

            // 初始化Harmony
            var harmony = new Harmony("tourswen.wulafallenempire"); // 替换为您的唯一Mod ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("[WulaFallenEmpire] Harmony patches applied.");
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
            Log.Message("WulaFallenEmpire Mod DLL, version 1.0.2, has been loaded.");
        }
    }
}
