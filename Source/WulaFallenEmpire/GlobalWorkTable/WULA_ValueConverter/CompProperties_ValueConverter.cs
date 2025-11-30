using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_ValueConverter : CompProperties_Launchable_TransportPod
    {
        public float conversionRatio = 0.5f; // 默认50%的转换比例
        public ThingDef targetCurrency = ThingDefOf.Silver; // 目标货币，默认为白银
        
        // 新增：垃圾屏蔽配置 - 专门为价值转换器配置
        public bool garbageShieldEnabled = true; // 默认启用垃圾屏蔽
        public string garbageShieldUIEventDefName = "Wula_UI_Legion_Reply_1"; // 默认UI事件
        public bool checkNonTradableItems = true; // 专门为价值转换器启用不可交易物品检查
        
        public CompProperties_ValueConverter()
        {
            this.compClass = typeof(CompValueConverter);
        }
    }
}
