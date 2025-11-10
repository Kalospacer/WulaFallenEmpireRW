using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class EnergyLanceExtension : DefModExtension
    {
        // 伤害类型配置
        public DamageDef damageDef = DamageDefOf.Flame;    // 伤害类型，默认为火焰伤害
        public bool applyDamageToCorpses = true;          // 是否对尸体造成伤害
        public bool igniteFires = true;                   // 是否点燃火焰
        public float fireIgniteChance = 0.8f;             // 点燃火焰的概率
        
        // 伤害量配置
        public IntRange damageAmountRange = new IntRange(65, 100);        // 对生物的伤害范围
        public IntRange corpseDamageAmountRange = new IntRange(5, 10);    // 对尸体的伤害范围
        
        // 视觉效果配置
        public ThingDef moteDef;                          // 使用的Mote类型
        public SoundDef impactSound;                      // 撞击音效
    }
}
