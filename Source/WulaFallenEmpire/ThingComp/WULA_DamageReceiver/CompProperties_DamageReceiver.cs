using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_DamageReceiver : CompProperties
    {
        public float maxDamageCapacity = 1000f; // 最大伤害容量
        public float damageDecayRate = 5f; // 每 tick 衰减的伤害量
        public float damageDecayInterval = 60f; // 伤害衰减间隔（ticks）
        public bool showDamageBar = true; // 是否显示伤害条
        public bool canBeDestroyedByDamage = true; // 是否可以被伤害摧毁
        
        public CompProperties_DamageReceiver()
        {
            compClass = typeof(CompDamageReceiver);
        }
    }
}
