using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class EnergyLanceExtension : DefModExtension
    {
        // 移动平滑配置
        public float moveSmoothing = 0.1f;         // 移动平滑度 (0-1)，值越小越平滑
        public int moteSpawnInterval = 3;          // Mote生成间隔，值越大密度越低
        public float moteScale = 0.8f;             // Mote缩放比例
        
        // 伤害配置
        public int firesPerTick = 4;
        public float effectRadius = 15f;
    }
}
