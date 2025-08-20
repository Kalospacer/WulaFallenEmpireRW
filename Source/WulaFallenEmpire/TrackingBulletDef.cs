using Verse;

namespace WulaFallenEmpire
{
    public class TrackingBulletDef : DefModExtension
    {
        public float homingSpeed = 0.1f; // 追踪速度，值越大追踪越灵敏
        public float initRotateAngle = 0f; // 初始旋转角度
    }
}