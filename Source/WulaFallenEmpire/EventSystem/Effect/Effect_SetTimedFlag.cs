// 在 EffectBase.cs 中添加以下类
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class Effect_SetTimedFlag : EffectBase
    {
        public string flagName;
        public int durationTicks; // 持续时间（tick），负数表示永久

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(flagName))
            {
                WulaLog.Debug("[WulaFallenEmpire] Effect_SetTimedFlag has a null or empty flagName.");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            eventVarManager.SetTimedFlag(flagName, durationTicks);

            string durationInfo = durationTicks < 0 ? "permanent" : $"{durationTicks} ticks";
            WulaLog.Debug($"[EventSystem] Set timed flag '{flagName}' with duration: {durationInfo}");
        }
    }
}