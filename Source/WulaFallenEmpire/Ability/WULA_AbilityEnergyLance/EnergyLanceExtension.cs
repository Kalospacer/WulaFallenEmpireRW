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

        // 新增：移动速度配置
        public float flightSpeed = 5f;             // 光束移动速度（格/秒）
        public float acceleration = 2f;            // 加速度
        public float maxSpeed = 10f;               // 最大速度
        public float turnSpeed = 90f;              // 转向速度（度/秒）

        // 移动模式配置
        public bool useDynamicMovement = true;     // 使用动态追踪模式
        public bool snapToTarget = false;          // 是否直接瞬移到目标
        public float snapDistance = 3f;            // 瞬移触发距离

        // 行为配置
        public bool hoverOverTarget = true;        // 在目标上空悬停
        public float hoverAltitude = 20f;          // 悬停高度
        public float attackRange = 15f;            // 攻击范围
    }
}