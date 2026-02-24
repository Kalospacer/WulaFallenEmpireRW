using Verse;

namespace WulaFallenEmpire
{
    // 定义了在XML中可以设置的属性
    public class HediffCompProperties_DamageResponse : HediffCompProperties
    {
        // 每点伤害值所转化的严重性数值
        public float severityIncreasePerDamage = 0f;

        public HediffCompProperties_DamageResponse()
        {
            this.compClass = typeof(HediffComp_DamageResponse);
        }
    }

    // 组件的实际逻辑
    public class HediffComp_DamageResponse : HediffComp
    {
        // 一个方便获取上面属性的捷径
        private HediffCompProperties_DamageResponse Props => (HediffCompProperties_DamageResponse)this.props;

        // 当Hediff的持有者（Pawn）受到伤害后，这个方法会被游戏调用
        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);

            // 如果伤害值大于0，并且我们在XML中设置了转化率
            if (totalDamageDealt > 0 && Props.severityIncreasePerDamage > 0f)
            {
                // 增加父Hediff的严重性
                // this.parent 指向这个组件所属的Hediff实例
                this.parent.Severity += totalDamageDealt * Props.severityIncreasePerDamage;
            }
        }
    }
}