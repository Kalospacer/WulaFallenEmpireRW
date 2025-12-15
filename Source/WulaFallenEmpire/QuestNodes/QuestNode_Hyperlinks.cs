using RimWorld;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class QuestNode_Hyperlinks : QuestNode
    {
        // 输入参数 - 使用 SlateRef 来支持从任务slate中获取值
        public SlateRef<List<ThingDef>> thingDefs;
        public SlateRef<List<Pawn>> pawns;
        public SlateRef<List<FactionDef>> factionDefs;  // 改为 FactionDef 而不是 Faction
        public SlateRef<List<ResearchProjectDef>> researchProjects;

        protected override bool TestRunInt(Slate slate)
        {
            // 测试模式下，只要参数有效就返回true
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;

            // 获取实际的值
            List<ThingDef> actualThingDefs = thingDefs.GetValue(slate);
            List<Pawn> actualPawns = pawns.GetValue(slate);
            List<FactionDef> actualFactionDefs = factionDefs.GetValue(slate);
            List<ResearchProjectDef> actualResearchProjects = researchProjects.GetValue(slate);

            // 创建任务部分
            QuestPart_Hyperlinks hyperlinksPart = new QuestPart_Hyperlinks();

            // 设置超链接数据
            if (actualThingDefs != null)
            {
                hyperlinksPart.thingDefs.AddRange(actualThingDefs);
            }

            if (actualPawns != null)
            {
                hyperlinksPart.pawns.AddRange(actualPawns);
            }

            if (actualFactionDefs != null)
            {
                // 将 FactionDef 转换为 Faction
                foreach (FactionDef factionDef in actualFactionDefs)
                {
                    Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
                    if (faction != null)
                    {
                        hyperlinksPart.factions.Add(faction);
                    }
                    else
                    {
                        WulaLog.Debug($"QuestNode_Hyperlinks: Could not find faction for def {factionDef.defName}");
                    }
                }
            }

            if (actualResearchProjects != null)
            {
                hyperlinksPart.researchProjects.AddRange(actualResearchProjects);
            }

            // 添加到任务
            quest.AddPart(hyperlinksPart);

            WulaLog.Debug($"QuestNode_Hyperlinks: Added hyperlinks - Things: {actualThingDefs?.Count ?? 0}, Pawns: {actualPawns?.Count ?? 0}, Factions: {actualFactionDefs?.Count ?? 0}, Research: {actualResearchProjects?.Count ?? 0}");
        }
    }
}
