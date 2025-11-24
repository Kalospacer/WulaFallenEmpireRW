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
        
        // 新增：有时限的flag字典
        private Dictionary<string, int> timedFlags = new Dictionary<string, int>();

        // 用于Scribe的辅助列表
        private List<string> pawnVarKeys;
        private List<Pawn> pawnVarValues;
        private List<string> pawnListVarKeys;
        private List<List<Pawn>> pawnListVarValues;
        private List<string> timedFlagKeys;
        private List<int> timedFlagValues;

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
            Scribe_Collections.Look(ref timedFlags, "timedFlags", LookMode.Value, LookMode.Value, ref timedFlagKeys, ref timedFlagValues);

            // Ensure dictionaries are not null after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                intVars ??= new Dictionary<string, int>();
                floatVars ??= new Dictionary<string, float>();
                stringVars ??= new Dictionary<string, string>();
                pawnVars ??= new Dictionary<string, Pawn>();
                pawnListVars ??= new Dictionary<string, List<Pawn>>();
                timedFlags ??= new Dictionary<string, int>();
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            
            // 每60 tick检查一次过期flag
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CheckExpiredFlags();
            }
        }

        /// <summary>
        /// 检查并清理过期的flag
        /// </summary>
        private void CheckExpiredFlags()
        {
            List<string> flagsToRemove = new List<string>();
            int currentTick = Find.TickManager.TicksGame;

            foreach (var kvp in timedFlags)
            {
                // 如果flag的过期时间不为负数且小于当前tick，则标记为需要移除
                if (kvp.Value >= 0 && currentTick >= kvp.Value)
                {
                    flagsToRemove.Add(kvp.Key);
                    Log.Message($"[EventSystem] Flag '{kvp.Key}' expired and will be removed.");
                }
            }

            // 移除过期的flag
            foreach (string flagName in flagsToRemove)
            {
                timedFlags.Remove(flagName);
            }
        }

        public void SetVariable(string name, object value)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Log the variable change
            Log.Message($"[EventSystem] Setting variable '{name}' to value '{value}' of type {value?.GetType().Name ?? "null"}.");

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

        /// <summary>
        /// 设置有时限的flag
        /// </summary>
        /// <param name="flagName">flag名称</param>
        /// <param name="durationTicks">持续时间（tick），负数表示永久</param>
        public void SetTimedFlag(string flagName, int durationTicks)
        {
            if (string.IsNullOrEmpty(flagName)) return;

            int expiryTick;
            if (durationTicks < 0)
            {
                // 负数表示永久flag
                expiryTick = -1;
                Log.Message($"[EventSystem] Setting permanent flag '{flagName}'.");
            }
            else
            {
                // 正数表示有时间限制的flag
                expiryTick = Find.TickManager.TicksGame + durationTicks;
                Log.Message($"[EventSystem] Setting timed flag '{flagName}' with duration {durationTicks} ticks (expires at tick {expiryTick}).");
            }

            timedFlags[flagName] = expiryTick;
        }

        /// <summary>
        /// 检查flag是否存在且未过期
        /// </summary>
        public bool HasFlag(string flagName)
        {
            if (string.IsNullOrEmpty(flagName)) return false;

            if (timedFlags.TryGetValue(flagName, out int expiryTick))
            {
                if (expiryTick < 0)
                {
                    // 永久flag
                    return true;
                }
                else
                {
                    // 检查是否过期
                    bool isActive = Find.TickManager.TicksGame < expiryTick;
                    if (!isActive)
                    {
                        // 如果过期了，移除它
                        timedFlags.Remove(flagName);
                        Log.Message($"[EventSystem] Flag '{flagName}' has expired and was removed.");
                    }
                    return isActive;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取flag的剩余时间（tick）
        /// </summary>
        public int GetFlagRemainingTicks(string flagName)
        {
            if (string.IsNullOrEmpty(flagName) || !timedFlags.TryGetValue(flagName, out int expiryTick))
                return 0;

            if (expiryTick < 0)
            {
                // 永久flag
                return -1;
            }

            int remaining = expiryTick - Find.TickManager.TicksGame;
            return remaining > 0 ? remaining : 0;
        }

        public T GetVariable<T>(string name, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(name)) return defaultValue;

            object value = null;
            if (pawnListVars.TryGetValue(name, out var pawnListVal))
            {
                value = pawnListVal;
            }
            else if (pawnVars.TryGetValue(name, out var pawnVal))
            {
                value = pawnVal;
            }
            else if (floatVars.TryGetValue(name, out var floatVal))
            {
                value = floatVal;
            }
            else if (intVars.TryGetValue(name, out var intVal))
            {
                value = intVal;
            }
            else if (stringVars.TryGetValue(name, out var stringVal))
            {
                value = stringVal;
            }

            if (value != null)
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                try
                {
                    // Handle cases where T is object but the stored value is, e.g., an int
                    if (typeof(T) == typeof(object))
                    {
                        return (T)value;
                    }
                    return (T)System.Convert.ChangeType(value, typeof(T));
                }
                catch (System.Exception e)
                {
                    Log.Warning($"[WulaFallenEmpire] EventVariableManager: Variable '{name}' of type {value.GetType()} could not be converted to {typeof(T)}. Error: {e.Message}");
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
                   pawnListVars.ContainsKey(name) ||
                   timedFlags.ContainsKey(name);
        }

        public void ClearVariable(string name)
        {
            if (HasVariable(name))
            {
                Log.Message($"[EventSystem] Clearing variable '{name}'.");
            }
            intVars.Remove(name);
            floatVars.Remove(name);
            stringVars.Remove(name);
            pawnVars.Remove(name);
            pawnListVars.Remove(name);
            timedFlags.Remove(name);
        }
        
        public void ClearAll()
        {
            intVars.Clear();
            floatVars.Clear();
            stringVars.Clear();
            pawnVars.Clear();
            pawnListVars.Clear();
            timedFlags.Clear();
        }

        public Dictionary<string, object> GetAllVariables()
        {
            var allVars = new Dictionary<string, object>();
            foreach (var kvp in intVars) allVars[kvp.Key] = kvp.Value;
            foreach (var kvp in floatVars) allVars[kvp.Key] = kvp.Value;
            foreach (var kvp in stringVars) allVars[kvp.Key] = kvp.Value;
            foreach (var kvp in pawnVars) allVars[kvp.Key] = kvp.Value;
            foreach (var kvp in pawnListVars) allVars[kvp.Key] = kvp.Value;
            foreach (var kvp in timedFlags) allVars[kvp.Key] = $"Flag (expires: {kvp.Value})";
            return allVars;
        }
    }
}
