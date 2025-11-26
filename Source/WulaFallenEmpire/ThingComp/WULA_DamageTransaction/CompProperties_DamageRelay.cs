using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_DamageRelay : CompProperties
    {
        public float damageRelayRatio = 0.3f; // 继续传递伤害的比例
        public bool relayOnlyToSameFaction = true; // 是否只传递给同派系建筑
        public FloatRange healthThreshold = new FloatRange(0.1f, 1f); // 生命值阈值（低于此值才开始传递）
        
        // 效果设置
        public EffecterDef relayEffecter;
        public SoundDef relaySound;
        
        public CompProperties_DamageRelay()
        {
            compClass = typeof(CompDamageRelay);
        }
    }
}
