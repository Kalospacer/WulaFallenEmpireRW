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
        public WulaFallenEmpireMod(ModContentPack content) : base(content)
        {
            // 初始化Harmony
            var harmony = new Harmony("tourswen.wulafallenempire"); // 替换为您的唯一Mod ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("[WulaFallenEmpire] Harmony patches applied.");
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
