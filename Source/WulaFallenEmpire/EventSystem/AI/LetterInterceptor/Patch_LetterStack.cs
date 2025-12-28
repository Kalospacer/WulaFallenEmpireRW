using System;
using HarmonyLib;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.LetterInterceptor
{
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new Type[] { typeof(Letter), typeof(string) })]
    public static class Patch_LetterStack_ReceiveLetter
    {
        public static void Postfix(Letter let, string debugInfo)
        {
            var settings = WulaFallenEmpireMod.settings;
            if (settings == null || !settings.enableAIAutoCommentary)
            {
                return;
            }

            if (let == null)
            {
                return;
            }

            AIAutoCommentary.ProcessLetter(let);
        }
    }
}
