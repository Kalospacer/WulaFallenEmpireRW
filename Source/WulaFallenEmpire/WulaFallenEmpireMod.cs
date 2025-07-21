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
            
            // 手动应用护盾腰带的近战拦截补丁
            WulaShieldBeltPatches.ApplyMeleePatch(harmony);

            Log.Message("[WulaFallenEmpire] Harmony patches applied.");
        }
    }
}
