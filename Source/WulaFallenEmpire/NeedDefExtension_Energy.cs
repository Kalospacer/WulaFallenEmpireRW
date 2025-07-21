using Verse;

namespace WulaFallenEmpire
{
    public class NeedDefExtension_Energy : DefModExtension
    {
        // 能量每天的消耗值
        public float fallPerDay = 1.6f;
        // 能量上限
        public float maxLevel = 1.0f;
        // 运送能量的阈值
        public float deliverEnergyThreshold = 0.5f;
        // 自动摄取能量的阈值
        public float autoIngestThreshold = 0.5f;
    }
}
