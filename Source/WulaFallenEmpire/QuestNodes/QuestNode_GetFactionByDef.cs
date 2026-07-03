using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 按 FactionDef 查找世界中的派系实例并存入 slate 变量。
    /// 原版 QuestNode_GetFaction 只支持按关系条件筛选，无法直接指定 defName，
    /// 用于给 QuestNode_GenerateSite 等节点提供固定派系。
    /// </summary>
    public class QuestNode_GetFactionByDef : QuestNode
    {
        public SlateRef<FactionDef> factionDef;

        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate)
        {
            return TrySetFaction(slate);
        }

        protected override void RunInt()
        {
            TrySetFaction(QuestGen.slate);
        }

        private bool TrySetFaction(Slate slate)
        {
            FactionDef def = factionDef.GetValue(slate);
            if (def == null)
            {
                WulaLog.Debug("QuestNode_GetFactionByDef: factionDef is null");
                return false;
            }
            Faction faction = Find.FactionManager.FirstFactionOfDef(def);
            if (faction == null)
            {
                WulaLog.Debug($"QuestNode_GetFactionByDef: no faction of def {def.defName} in world");
                return false;
            }
            slate.Set(storeAs.GetValue(slate), faction);
            return true;
        }
    }
}
