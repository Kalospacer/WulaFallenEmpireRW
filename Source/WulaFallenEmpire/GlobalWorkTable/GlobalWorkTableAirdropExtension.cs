using Verse;

namespace WulaFallenEmpire
{
    public class GlobalWorkTableAirdropExtension : DefModExtension
    {
        public float maxRange = 50f; // 最大空投范围
        public float randomRange = 15f; // 随机散布范围
        public int minPods = 1; // 最少空投舱数量
        public int maxPods = 10; // 最多空投舱数量
        
        // 必须添加无参数构造函数
        public GlobalWorkTableAirdropExtension() { }
        
        // 可以添加其他参数，比如冷却时间、消耗资源等
    }
}
