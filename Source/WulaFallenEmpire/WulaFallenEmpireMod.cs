using System;
using System.Reflection;
using HarmonyLib;
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
}
