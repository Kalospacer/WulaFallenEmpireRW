using Verse;

namespace WulaFallenEmpire
{
    public class TrackingBulletDef : DefModExtension
    {
        public float homingSpeed = 0.1f; // 追踪速度，值越大追踪越灵敏
        public float initRotateAngle = 0f; // 初始旋转角度
        
        public FleckDef tailFleckDef; // 拖尾特效的FleckDef
        public int fleckMakeFleckTickMax = 1; // 拖尾特效的生成间隔（tick）
        public int fleckDelayTicks = 10; // 拖尾特效延迟生成时间（tick）
        public IntRange fleckMakeFleckNum = new IntRange(1, 1); // 每次生成拖尾特效的数量
        public FloatRange fleckAngle = new FloatRange(-180f, 180f); // 拖尾特效的初始角度范围
        public FloatRange fleckScale = new FloatRange(1f, 1f); // 拖尾特效的缩放范围
        public FloatRange fleckSpeed = new FloatRange(0f, 0f); // 拖尾特效的初始速度范围
        public FloatRange fleckRotation = new FloatRange(-180f, 180f); // 拖尾特效的旋转速度范围
        public IntRange destroyTicksAfterLosingTrack = new IntRange(60, 120); // 失去追踪后多少tick自毁
    }
}