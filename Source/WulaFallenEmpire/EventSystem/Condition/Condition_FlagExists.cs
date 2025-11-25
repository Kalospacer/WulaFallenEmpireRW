using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public abstract class Condition
    {
        public abstract bool IsMet(out string reason);
    }

    public class Condition_FlagExists : ConditionBase
    {
        public string flagName;

        public override bool IsMet(out string reason)
        {
            if (string.IsNullOrEmpty(flagName))
            {
                reason = "Flag name is not specified.";
                return false;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            bool flagExists = eventVarManager.HasFlag(flagName);

            if (!flagExists)
            {
                reason = $"Flag '{flagName}' does not exist or has expired.";
            }
            else
            {
                int remainingTicks = eventVarManager.GetFlagRemainingTicks(flagName);
                if (remainingTicks < 0)
                {
                    reason = $"Flag '{flagName}' exists (permanent).";
                }
                else
                {
                    reason = $"Flag '{flagName}' exists (expires in {remainingTicks} ticks).";
                }
            }

            Log.Message($"[EventSystem] Condition_FlagExists check: Flag='{flagName}', Exists={flagExists}, Reason='{reason}'");
            return flagExists;
        }
    }

    public class Condition_FlagNotExists : ConditionBase
    {
        public string flagName;

        public override bool IsMet(out string reason)
        {
            if (string.IsNullOrEmpty(flagName))
            {
                reason = "Flag name is not specified.";
                return false;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            bool flagExists = eventVarManager.HasFlag(flagName);

            if (flagExists)
            {
                int remainingTicks = eventVarManager.GetFlagRemainingTicks(flagName);
                if (remainingTicks < 0)
                {
                    reason = $"Flag '{flagName}' exists (permanent).";
                }
                else
                {
                    reason = $"Flag '{flagName}' exists (expires in {remainingTicks} ticks).";
                }
                return false;
            }
            else
            {
                reason = $"Flag '{flagName}' does not exist.";
                return true;
            }
        }
    }
}
