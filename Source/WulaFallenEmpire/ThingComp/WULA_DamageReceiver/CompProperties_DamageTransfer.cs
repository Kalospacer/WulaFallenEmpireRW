using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_DamageTransfer : CompProperties
    {
        public float damageTransferRatio = 0.8f; // 伤害转移比例 (80%)
        public float maxTransferRange = 30f; // 最大转移范围
        public bool requireLineOfSight = false; // 是否需要视线
        public bool transferAllDamageTypes = true; // 是否转移所有伤害类型
        public FloatRange healthThreshold = new FloatRange(0f, 1f); // 生命值阈值（低于此值才触发）
        
        // 效果设置
        public EffecterDef transferEffecter;
        public SoundDef transferSound;
        
        public CompProperties_DamageTransfer()
        {
            compClass = typeof(CompDamageTransfer);
        }
    }
}
