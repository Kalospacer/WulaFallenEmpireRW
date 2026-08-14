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
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, 300f); // Adjust if more height is needed
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);

            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(viewRect);

            listingStandard.CheckboxLabeled("Wula_EnableDebugLogs".Translate(), ref settings.enableDebugLogs, "Wula_EnableDebugLogsDesc".Translate());

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
