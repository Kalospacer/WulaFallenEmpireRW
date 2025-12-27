using System;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 征召殖民者工具 - 将殖民者置于征召状态以便直接控制
    /// </summary>
    public class Tool_DraftPawn : AITool
    {
        public override string Name => "draft_pawn";
        
        public override string Description => 
            "征召或解除征召殖民者。征召后可以直接控制殖民者移动和攻击。";
        
        public override string UsageSchema => 
            "<draft_pawn><pawn_name>殖民者名字</pawn_name><draft>true征召/false解除</draft></draft_pawn>";
        
        public override string Execute(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                // 解析殖民者名字
                if (!argsDict.TryGetValue("pawn_name", out string pawnName) || string.IsNullOrWhiteSpace(pawnName))
                {
                    // 尝试其他常见参数名
                    if (!argsDict.TryGetValue("name", out pawnName) || string.IsNullOrWhiteSpace(pawnName))
                    {
                        return "Error: 缺少殖民者名字 (pawn_name)";
                    }
                }
                
                // 解析征召状态
                bool draft = true;
                if (argsDict.TryGetValue("draft", out string draftStr))
                {
                    draft = draftStr.ToLowerInvariant() != "false" && draftStr != "0";
                }
                
                // 获取地图
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return "Error: 没有活动的地图";
                }
                
                // 查找殖民者
                Pawn targetPawn = null;
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.LabelShortCap.Equals(pawnName, StringComparison.OrdinalIgnoreCase) ||
                        pawn.Name?.ToStringShort?.Equals(pawnName, StringComparison.OrdinalIgnoreCase) == true ||
                        pawn.LabelCap.ToString().IndexOf(pawnName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetPawn = pawn;
                        break;
                    }
                }
                
                if (targetPawn == null)
                {
                    return $"Error: 找不到殖民者 '{pawnName}'";
                }
                
                // 检查是否可以征召
                if (targetPawn.Downed)
                {
                    return $"Error: {targetPawn.LabelShortCap} 已倒地，无法征召";
                }
                
                if (targetPawn.Dead)
                {
                    return $"Error: {targetPawn.LabelShortCap} 已死亡";
                }
                
                if (targetPawn.drafter == null)
                {
                    return $"Error: {targetPawn.LabelShortCap} 无法被征召";
                }
                
                // 执行征召/解除
                bool wasDrafted = targetPawn.Drafted;
                targetPawn.drafter.Drafted = draft;
                
                string action = draft ? "征召" : "解除征召";
                
                if (wasDrafted == draft)
                {
                    return $"Info: {targetPawn.LabelShortCap} 已经处于{(draft ? "征召" : "非征召")}状态";
                }
                
                Messages.Message($"AI: {action}了 {targetPawn.LabelShortCap}", targetPawn, MessageTypeDefOf.NeutralEvent);
                return $"Success: 已{action} {targetPawn.LabelShortCap}，当前位置 ({targetPawn.Position.x}, {targetPawn.Position.z})";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_DraftPawn] Error: {ex}");
                return $"Error: 征召操作失败 - {ex.Message}";
            }
        }
    }
}
