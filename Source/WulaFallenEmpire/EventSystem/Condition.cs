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
        public string valueVariableName;

        public override bool IsMet(out string reason)
        {
            object variable = EventContext.GetVariable<object>(name);
            if (variable == null)
            {
                reason = $"Variable '{name}' not set.";
                return false;
            }

            string compareValue = value;
            if (!string.IsNullOrEmpty(valueVariableName))
            {
                compareValue = EventContext.GetVariable<object>(valueVariableName)?.ToString();
                if (compareValue == null)
                {
                    reason = $"Comparison variable '{valueVariableName}' not set.";
                    return false;
                }
            }

            bool met = variable.ToString() == compareValue;
            if (!met)
            {
                reason = $"Requires {name} = {compareValue} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }

    public abstract class Condition_CompareVariable : Condition
    {
        public string name;
        public float value;
        public string valueVariableName;

        protected abstract bool Compare(float var1, float var2);
        protected abstract string GetOperatorString();

        public override bool IsMet(out string reason)
        {
            float variable = EventContext.GetVariable<float>(name, float.NaN);
            if (float.IsNaN(variable))
            {
                reason = $"Variable '{name}' not set or not a number.";
                return false;
            }

            float compareValue = value;
            if (!string.IsNullOrEmpty(valueVariableName))
            {
                compareValue = EventContext.GetVariable<float>(valueVariableName, float.NaN);
                if (float.IsNaN(compareValue))
                {
                    reason = $"Comparison variable '{valueVariableName}' not set or not a number.";
                    return false;
                }
            }

            bool met = Compare(variable, compareValue);
            if (!met)
            {
                reason = $"Requires {name} {GetOperatorString()} {compareValue} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }

    public class Condition_VariableGreaterThan : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 > var2;
        protected override string GetOperatorString() => ">";
    }

    public class Condition_VariableLessThan : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 < var2;
        protected override string GetOperatorString() => "<";
    }

    public class Condition_VariableGreaterThanOrEqual : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 >= var2;
        protected override string GetOperatorString() => ">=";
    }

    public class Condition_VariableLessThanOrEqual : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 <= var2;
        protected override string GetOperatorString() => "<=";
    }

    public class Condition_VariableNotEqual : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 != var2;
        protected override string GetOperatorString() => "!=";
    }
}
