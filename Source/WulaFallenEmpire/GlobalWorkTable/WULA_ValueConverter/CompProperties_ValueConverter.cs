using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_ValueConverter : CompProperties
    {
        public float conversionRate = 1.0f; // 价值转换倍率
        public ThingDef outputThingDef = null; // 输出物品定义，默认为白银
        public bool destroyAfterConversion = true; // 转换后是否销毁建筑
        
        // 垃圾屏蔽配置
        public bool garbageShieldEnabled = false;
        public string garbageShieldUIEventDefName = "Wula_UI_Legion_Reply_1";

        public CompProperties_ValueConverter()
        {
            this.compClass = typeof(CompValueConverter);
        }
    }
}
