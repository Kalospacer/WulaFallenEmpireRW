using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
    public class WulaSkyfallerWorldComponent : WorldComponent
    {
        // 默认为 true，即自动召唤
        public bool AutoCallSkyfaller = true;

        public WulaSkyfallerWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AutoCallSkyfaller, "AutoCallSkyfaller", true);
        }
    }
}