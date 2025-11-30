using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_Launchable_ToGlobalStorage : CompProperties_Launchable_TransportPod
    {
        public float fuelNeededToLaunch = 25f;
        public SoundDef launchSound;
        
        // 垃圾屏蔽配置 - 通过XML控制
        public bool garbageShieldEnabled = false;
        public string garbageShieldUIEventDefName = "Wula_UI_Legion_Reply_1";
        // 新增：明确不检查不可交易物品
        public bool checkNonTradableItems = false; // Launchable_ToGlobalStorage 不需要检查不可交易物品

        public CompProperties_Launchable_ToGlobalStorage()
        {
            this.compClass = typeof(CompLaunchable_ToGlobalStorage);
        }
    }
}
