using System;
using HarmonyLib;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.LetterInterceptor
{
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), 
        new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
    public static class Patch_LetterStack_ReceiveLetter
    {
        public static void Postfix(Letter let, string debugInfo, int delayTicks, bool playSound)
        {
            // Only process if not delayed (delayTicks == 0 or already arrived)
            if (delayTicks > 0) return;
            
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
