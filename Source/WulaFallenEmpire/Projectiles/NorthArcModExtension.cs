using Verse;

namespace WulaFallenEmpire
{
    public class NorthArcModExtension : DefModExtension
    {
        // 控制向北偏移的高度（格数），值越大弧度越高
        public float northOffsetDistance = 10f;
        
        // 控制曲线的形状，值越大曲线越陡峭
        public float curveSteepness = 1f;
        
        // 是否使用弧形轨迹（默认为true，如果为false则使用直线轨迹）
        public bool useArcTrajectory = true;
    }
}