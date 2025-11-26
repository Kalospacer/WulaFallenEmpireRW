using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_DamageInterceptor : CompProperties
    {
        public float damageTransferRatio = 1f; // 完全拦截并转移伤害
        public string targetBuildingDefName = "WULA_Sky_Lock"; // 目标建筑类型
        public bool requireSameFaction = true; // 是否需要同派系
        public FloatRange healthThreshold = new FloatRange(0f, 1f); // 生命值阈值范围
        
        // 效果设置
        public EffecterDef interceptEffecter;
        public SoundDef interceptSound;
        
        public CompProperties_DamageInterceptor()
        {
            compClass = typeof(CompDamageInterceptor);
        }
    }
}
