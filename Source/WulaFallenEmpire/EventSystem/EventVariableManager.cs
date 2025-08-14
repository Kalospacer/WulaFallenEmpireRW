using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    public class EventVariableManager : WorldComponent
    {
        private Dictionary<string, int> intVars = new Dictionary<string, int>();
        private Dictionary<string, float> floatVars = new Dictionary<string, float>();
        private Dictionary<string, string> stringVars = new Dictionary<string, string>();
        private Dictionary<string, Pawn> pawnVars = new Dictionary<string, Pawn>();
        private Dictionary<string, List<Pawn>> pawnListVars = new Dictionary<string, List<Pawn>>();

        // 用于Scribe的辅助列表
        private List<string> pawnVarKeys;
        private List<Pawn> pawnVarValues;
        private List<string> pawnListVarKeys;
        private List<List<Pawn>> pawnListVarValues;

        // Required for WorldComponent
        public EventVariableManager(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref intVars, "intVars", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref floatVars, "floatVars", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref stringVars, "stringVars", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref pawnVars, "pawnVars", LookMode.Value, LookMode.Reference, ref pawnVarKeys, ref pawnVarValues);
            Scribe_Collections.Look(ref pawnListVars, "pawnListVars", LookMode.Value, LookMode.Reference, ref pawnListVarKeys, ref pawnListVarValues);

            // Ensure dictionaries are not null after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                intVars ??= new Dictionary<string, int>();
                floatVars ??= new Dictionary<string, float>();
                stringVars ??= new Dictionary<string, string>();
                pawnVars ??= new Dictionary<string, Pawn>();
                pawnListVars ??= new Dictionary<string, List<Pawn>>();
            }
        }

        public void SetVariable(string name, object value)
        {
            if (string.IsNullOrEmpty(name)) return;
            
            // Clear any existing variable with the same name to prevent type confusion
            ClearVariable(name);

            if (value is int intValue)
            {
                intVars[name] = intValue;
            }
            else if (value is float floatValue)
            {
                floatVars[name] = floatValue;
            }
            else if (value is string stringValue)
            {
                stringVars[name] = stringValue;
            }
            else if (value is Pawn pawnValue)
            {
                pawnVars[name] = pawnValue;
            }
            else if (value is List<Pawn> pawnListValue)
            {
                pawnListVars[name] = pawnListValue;
            }
            else if (value != null)
            {
                stringVars[name] = value.ToString();
                Log.Warning($"[WulaFallenEmpire] EventVariableManager: Variable '{name}' of type {value.GetType()} was converted to string for storage. This may lead to unexpected behavior.");
            }
        }

        public T GetVariable<T>(string name, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(name)) return defaultValue;

            object value = null;
            bool found = false;

            if (typeof(T) == typeof(List<Pawn>) && pawnListVars.TryGetValue(name, out var pawnListVal))
            {
                value = pawnListVal;
                found = true;
            }
            else if (typeof(T) == typeof(Pawn) && pawnVars.TryGetValue(name, out var pawnVal))
            {
                value = pawnVal;
                found = true;
            }
            else if (typeof(T) == typeof(float) && floatVars.TryGetValue(name, out var floatVal))
            {
                value = floatVal;
                found = true;
            }
            else if (typeof(T) == typeof(int) && intVars.TryGetValue(name, out var intVal))
            {
                value = intVal;
                found = true;
            }
            else if (stringVars.TryGetValue(name, out var stringVal))
            {
                value = stringVal;
                found = true;
            }

            if (found)
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                try
                {
                    return (T)System.Convert.ChangeType(value, typeof(T));
                }
                catch (System.Exception)
                {
                     Log.Warning($"[WulaFallenEmpire] EventVariableManager: Variable '{name}' of type {value.GetType()} could not be converted to {typeof(T)}.");
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public bool HasVariable(string name)
        {
            return intVars.ContainsKey(name) ||
                   floatVars.ContainsKey(name) ||
                   stringVars.ContainsKey(name) ||
                   pawnVars.ContainsKey(name) ||
                   pawnListVars.ContainsKey(name);
        }

        public void ClearVariable(string name)
        {
            intVars.Remove(name);
            floatVars.Remove(name);
            stringVars.Remove(name);
            pawnVars.Remove(name);
            pawnListVars.Remove(name);
        }
        
        public void ClearAll()
        {
            intVars.Clear();
            floatVars.Clear();
            stringVars.Clear();
            pawnVars.Clear();
            pawnListVars.Clear();
        }
    }
}