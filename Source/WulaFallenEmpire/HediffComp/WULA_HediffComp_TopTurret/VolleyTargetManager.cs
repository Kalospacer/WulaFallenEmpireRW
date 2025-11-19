using System.Collections.Generic;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public static class VolleyTargetManager
    {
        private static Dictionary<Pawn, LocalTargetInfo> volleyTargets = new Dictionary<Pawn, LocalTargetInfo>();
        private static Dictionary<Pawn, bool> volleyEnabled = new Dictionary<Pawn, bool>();

        public static void SetVolleyTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn == null) return;
            
            volleyTargets[pawn] = target;
            volleyEnabled[pawn] = target.IsValid;
        }

        public static void ClearVolleyTarget(Pawn pawn)
        {
            if (pawn == null) return;
            
            volleyTargets.Remove(pawn);
            volleyEnabled.Remove(pawn);
        }

        public static LocalTargetInfo GetVolleyTarget(Pawn pawn)
        {
            if (pawn == null || !volleyTargets.ContainsKey(pawn))
                return LocalTargetInfo.Invalid;
                
            return volleyTargets[pawn];
        }

        public static bool IsVolleyEnabled(Pawn pawn)
        {
            if (pawn == null || !volleyEnabled.ContainsKey(pawn))
                return false;
                
            return volleyEnabled[pawn];
        }

        public static void ToggleVolley(Pawn pawn)
        {
            if (pawn == null) return;
            
            bool current = IsVolleyEnabled(pawn);
            volleyEnabled[pawn] = !current;
            
            // 如果禁用齐射，清除目标
            if (!volleyEnabled[pawn])
            {
                ClearVolleyTarget(pawn);
            }
        }
    }
}
