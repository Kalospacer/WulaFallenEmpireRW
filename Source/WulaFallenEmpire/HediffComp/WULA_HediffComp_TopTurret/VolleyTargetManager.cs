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
            
            Log.Message($"Set volley target for {pawn.Label}: {target.Thing?.Label ?? target.Cell.ToString()}");
        }

        public static void ClearVolleyTarget(Pawn pawn)
        {
            if (pawn == null) return;
            
            volleyTargets.Remove(pawn);
            volleyEnabled.Remove(pawn);
            
            Log.Message($"Cleared volley target for {pawn.Label}");
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
            
            Log.Message($"Toggled volley for {pawn.Label}: {!current}");
            
            // 如果禁用齐射，清除目标
            if (!volleyEnabled[pawn])
            {
                ClearVolleyTarget(pawn);
            }
        }

        // 新增：检查齐射目标是否仍然有效
        public static bool IsVolleyTargetValid(Pawn pawn)
        {
            if (!IsVolleyEnabled(pawn))
                return false;

            LocalTargetInfo target = GetVolleyTarget(pawn);
            if (!target.IsValid)
                return false;

            // 检查目标是否还活着/存在
            if (target.Thing != null)
            {
                if (target.Thing.Destroyed)
                    return false;

                if (target.Thing is Pawn targetPawn && (targetPawn.Dead || targetPawn.Downed))
                    return false;
            }

            return true;
        }
    }
}
