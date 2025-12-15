using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_GarbageShield : CompProperties
    {
        public bool garbageShieldEnabled = false; // 通过XML配置启用/禁用
        public string garbageShieldUIEventDefName; // 垃圾屏蔽触发时弹出的UI事件defName
        
        // 新增：配置是否检查不可交易物品
        public bool checkNonTradableItems = false;
        
        public CompProperties_GarbageShield()
        {
            this.compClass = typeof(CompGarbageShield);
        }
    }

    public class CompGarbageShield : ThingComp
    {
        public CompProperties_GarbageShield Props => (CompProperties_GarbageShield)this.props;
        
        // 垃圾屏蔽状态完全由XML配置决定，不提供玩家切换
        public bool GarbageShieldEnabled => Props.garbageShieldEnabled;

        // 检查物品是否是被禁止的垃圾物品
        public bool IsForbiddenItem(Thing thing)
        {
            if (!GarbageShieldEnabled) return false;

            // 检查是否是殖民者
            if (thing is Pawn pawn)
                return true;

            // 检查是否是尸体
            if (thing.def.IsCorpse)
                return true;

            // 检查是否是有毒垃圾
            if (IsToxicWaste(thing))
                return true;

            // 新增：检查不可交易物品（如果配置启用）
            if (Props.checkNonTradableItems && IsNonTradableItem(thing))
                return true;

            return false;
        }

        // 获取所有禁止物品
        public List<Thing> GetForbiddenItems(ThingOwner container)
        {
            List<Thing> forbiddenItems = new List<Thing>();
            
            if (!GarbageShieldEnabled) return forbiddenItems;
            
            foreach (Thing item in container)
            {
                if (IsForbiddenItem(item))
                {
                    forbiddenItems.Add(item);
                }
            }
            
            return forbiddenItems;
        }

        // 判断是否为有毒垃圾
        private bool IsToxicWaste(Thing thing)
        {
            // 根据物品标签、类别或定义名称判断是否为有毒垃圾
            return thing.def == ThingDefOf.Wastepack;
        }

        // 新增：判断是否为不可交易物品
        private bool IsNonTradableItem(Thing thing)
        {
            // 检查 tradeability 是否为 None
            return thing.def.tradeability == Tradeability.None;
        }

        // 处理垃圾屏蔽触发并触发UI事件
        public void ProcessGarbageShieldTrigger(List<Thing> forbiddenItems)
        {
            if (forbiddenItems.Count > 0 && !string.IsNullOrEmpty(Props.garbageShieldUIEventDefName))
            {
                // 弹出指定的自定义UI
                EventDef uiDef = DefDatabase<EventDef>.GetNamed(Props.garbageShieldUIEventDefName, false);
                if (uiDef != null)
                {
                    Find.WindowStack.Add(new Dialog_CustomDisplay(uiDef));
                }
                else
                {
                    WulaLog.Debug($"[CompGarbageShield] Could not find EventDef named '{Props.garbageShieldUIEventDefName}'.");
                }
            }
        }
    }
}
