using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_FlyOverType : CompProperties
    {
        public int laneLevel = 0;                    // 航道等级
        public string flyOverType = "default";       // FlyOver 类型标识符
        public bool isRequiredForDrop = true;        // 是否是需要用于空投的类型
        
        public CompProperties_FlyOverType()
        {
            compClass = typeof(CompFlyOverType);
        }
    }

    public class CompFlyOverType : ThingComp
    {
        private CompProperties_FlyOverType Props => (CompProperties_FlyOverType)props;
        
        public int LaneLevel => Props.laneLevel;
        public string FlyOverType => Props.flyOverType;
        public bool IsRequiredForDrop => Props.isRequiredForDrop;

        public override void PostExposeData()
        {
            base.PostExposeData();
        }

        public override string CompInspectStringExtra()
        {
            return $"FlyOver Type: {FlyOverType}, Lane Level: {LaneLevel}";
        }
    }
}
