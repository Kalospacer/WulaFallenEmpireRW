using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public abstract class Condition
    {
        public abstract bool IsMet(out string reason);
    }

    public class Condition_VariableEquals : Condition
    {
        public string name;
        public string value;

        public override bool IsMet(out string reason)
        {
            object variable = EventContext.GetVariable<object>(name);
            if (variable == null)
            {
                reason = $"Variable '{name}' not set.";
                return false;
            }

            // Simple string comparison for now. Can be expanded.
            bool met = variable.ToString() == value;
            if (!met)
            {
                reason = $"Requires {name} = {value} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }
    
    public class Condition_VariableGreaterThan : Condition
    {
        public string name;
        public float value;

        public override bool IsMet(out string reason)
        {
            float variable = EventContext.GetVariable<float>(name, float.MinValue);
            if (variable == float.MinValue)
            {
                reason = $"Variable '{name}' not set.";
                return false;
            }

            bool met = variable > value;
            if (!met)
            {
                reason = $"Requires {name} > {value} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }

}
