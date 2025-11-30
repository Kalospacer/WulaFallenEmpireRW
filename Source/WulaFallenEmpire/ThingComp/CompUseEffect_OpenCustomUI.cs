using RimWorld;
using System;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_UseEffect_OpenCustomUI : CompProperties_UseEffect
    {
        public string uiDefName; // 要打开的UI的EventDef名称
        public bool requireFactionPlayer = true; // 是否要求玩家派系才能使用

        public CompProperties_UseEffect_OpenCustomUI()
        {
            this.compClass = typeof(CompUseEffect_OpenCustomUI);
        }
    }

    public class CompUseEffect_OpenCustomUI : CompUseEffect
    {
        public CompProperties_UseEffect_OpenCustomUI Props => (CompProperties_UseEffect_OpenCustomUI)this.props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            try
            {
                // 查找对应的EventDef
                EventDef uiDef = DefDatabase<EventDef>.GetNamed(Props.uiDefName, false);
                if (uiDef != null)
                {
                    // 创建并打开自定义UI窗口
                    Window window = (Window)Activator.CreateInstance(uiDef.windowType, uiDef);
                    Find.WindowStack.Add(window);
                    
                    Log.Message($"[CompUseEffect] Opened custom UI: {Props.uiDefName}");
                }
                else
                {
                    Log.Error($"[CompUseEffect] Could not find EventDef named '{Props.uiDefName}'");
                    Messages.Message($"Error: Could not find UI definition '{Props.uiDefName}'", MessageTypeDefOf.RejectInput);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[CompUseEffect] Error opening custom UI '{Props.uiDefName}': {ex}");
                Messages.Message($"Error opening UI: {ex.Message}", MessageTypeDefOf.RejectInput);
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            // 基础检查
            AcceptanceReport baseResult = base.CanBeUsedBy(p);
            if (!baseResult.Accepted)
                return baseResult;

            // 检查派系要求
            if (Props.requireFactionPlayer && parent.Faction != Faction.OfPlayer)
            {
                return "Must be player faction to use this".Translate();
            }

            // 检查EventDef是否存在
            if (DefDatabase<EventDef>.GetNamed(Props.uiDefName, false) == null)
            {
                return $"UI definition '{Props.uiDefName}' not found".Translate();
            }

            return true;
        }
    }
}
