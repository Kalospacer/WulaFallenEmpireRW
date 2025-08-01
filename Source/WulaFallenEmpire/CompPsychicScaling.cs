using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 这个组件的XML属性定义。允许在XML中配置加成系数。
    /// </summary>
    public class CompProperties_PsychicScaling : CompProperties
    {
        // 每点心灵敏感度（超出100%的部分）提供的伤害【增伤】乘数。
        public float damageMultiplierPerSensitivityPoint = 0.25f;

        // 每点心灵敏感度（低于100%的部分）提供的伤害【减伤】乘数。
        // 例如，系数为1时，50%敏感度将造成 1 - (1 - 0.5) * 1 = 0.5倍伤害。
        public float damageReductionMultiplierPerSensitivityPoint = 1f;

        public CompProperties_PsychicScaling()
        {
            compClass = typeof(CompPsychicScaling);
        }
    }

    /// <summary>
    /// 附加到武器上的实际组件。它本身只是一个标记，真正的逻辑在Harmony Patch中。
    /// </summary>
    public class CompPsychicScaling : ThingComp
    {
        public CompProperties_PsychicScaling Props => (CompProperties_PsychicScaling)props;
    }
}