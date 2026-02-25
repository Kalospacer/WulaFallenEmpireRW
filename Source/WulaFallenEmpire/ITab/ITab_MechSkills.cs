// File: ITab_MechSkills.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 专业版：使用网格系统布局
    /// </summary>
    public class ITab_MechSkills : ITab
    {
        private Vector2 scrollPosition = Vector2.zero;
        private string nameBuffer = "";
        private bool isRenaming = false;
        
        // 网格系统参数
        private const float GridSize = 8f;
        private const float HeaderRows = 4;     // 头部占4行
        private const float PilotRows = 4;      // 驾驶员信息占3行
        private const float TitleRows = 2;      // 技能标题占1行
        private const float SkillRows = 18;     // 技能区域占20行
        
        public ITab_MechSkills()
        {
            this.size = new Vector2(520f, 600f);
            this.labelKey = "DD_MechSkills".Translate();
        }
        
        protected override void FillTab()
        {
            var pawn = SelPawn;
            if (pawn == null)
                return;
                
            if (pawn.TryGetComp<CompMechSkillInheritance>() == null)
            {
                DrawError("DD_NoMechSkillComps".Translate());
                return;
            }
            
            DrawGridLayout(pawn);
        }
        
        private void DrawGridLayout(Pawn pawn)
        {
            // 创建网格
            float rowHeight = size.y / (HeaderRows + PilotRows + TitleRows + SkillRows);
            
            // 1. 头部区域
            Rect headerRect = new Rect(0, 0, size.x, rowHeight * HeaderRows);
            DrawGridHeader(headerRect, pawn, rowHeight);
            
            // 2. 驾驶员区域
            float curY = headerRect.yMax;
            var pilotComp = pawn.TryGetComp<CompMechPilotHolder>();
            
            if (pilotComp != null)
            {
                Rect pilotRect = new Rect(0, curY, size.x, rowHeight * PilotRows);
                DrawGridPilot(pilotRect, pilotComp, rowHeight);
                curY = pilotRect.yMax;
            }
            else
            {
                curY += rowHeight; // 空一行
            }
            
            // 3. 技能区域
            Rect skillsRect = new Rect(0, curY, size.x, size.y - curY);
            DrawGridSkills(skillsRect, pawn, rowHeight);
        }
        private void DrawGridHeader(Rect rect, Pawn pawn, float rowHeight)
        {
            Widgets.DrawMenuSection(rect);

            // 使用网格定位
            float padding = rowHeight * 0.5f;

            if (isRenaming)
            {
                DrawRenameMode(rect, pawn, rowHeight, padding);
            }
            else
            {
                DrawNormalMode(rect, pawn, rowHeight, padding);
            }

            // 状态行（第3-4行）- 始终显示
            DrawStatusLine(rect, pawn, rowHeight, padding);

            Text.Anchor = TextAnchor.UpperLeft;
        }
        private void DrawRenameMode(Rect rect, Pawn pawn, float rowHeight, float padding)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;

            // 计算输入框和按钮的总宽度
            float totalWidth = rect.width - padding * 2;
            float buttonWidth = 85f; // 每个按钮85像素
            float spacing = 10f; // 按钮间距

            // 计算可用宽度（减去按钮宽度）
            float availableWidth = totalWidth - (buttonWidth * 2 + spacing);

            // 输入框
            Rect inputRect = new Rect(
                padding,
                padding,
                availableWidth,
                rowHeight * 1.5f
            );

            nameBuffer = nameBuffer ?? pawn.Name?.ToStringShort ?? pawn.LabelShort;
            nameBuffer = Widgets.TextField(inputRect, nameBuffer);

            // 确认按钮
            Rect confirmRect = new Rect(
                inputRect.xMax + spacing,
                inputRect.y,
                buttonWidth,
                inputRect.height
            );

            // 取消按钮
            Rect cancelRect = new Rect(
                confirmRect.xMax + spacing,
                inputRect.y,
                buttonWidth,
                inputRect.height
            );

            // 绘制按钮
            if (Widgets.ButtonText(confirmRect, "OK".Translate()))
            {
                if (pawn != null && !string.IsNullOrEmpty(nameBuffer))
                {
                    pawn.Name = new NameSingle(nameBuffer, false);
                    isRenaming = false;
                }
            }

            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                isRenaming = false;
            }

            // 添加回车键支持
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                if (pawn != null && !string.IsNullOrEmpty(nameBuffer))
                {
                    pawn.Name = new NameSingle(nameBuffer, false);
                    isRenaming = false;
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                isRenaming = false;
                Event.current.Use();
            }
        }
        private void DrawNormalMode(Rect rect, Pawn pawn, float rowHeight, float padding)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;

            // 名称行（第1-2行）
            Rect nameRect = new Rect(
                padding,
                padding,
                rect.width * 0.5f - padding * 2,
                rowHeight * 1.5f
            );

            Widgets.Label(nameRect, pawn.Name?.ToStringShort ?? pawn.LabelShort);

            // 重命名按钮（右对齐）
            if (pawn.Faction?.IsPlayer == true)
            {
                // 计算按钮宽度
                float renameButtonWidth = 100f;

                Rect renameRect = new Rect(
                    rect.width - padding - renameButtonWidth,
                    padding,
                    renameButtonWidth,
                    rowHeight * 1.5f
                );

                if (Widgets.ButtonText(renameRect, "Rename".Translate()))
                {
                    isRenaming = true;
                    nameBuffer = pawn.Name?.ToStringShort ?? pawn.LabelShort;
                }
            }
        }
        private void DrawStatusLine(Rect rect, Pawn pawn, float rowHeight, float padding)
        {
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);

            // 状态行的y坐标取决于是否在重命名模式
            float statusY;
            if (isRenaming)
            {
                // 重命名模式下，状态行在输入框下方
                statusY = padding + rowHeight * 1.5f + padding * 0.5f;
            }
            else
            {
                // 正常模式下，状态行在名称标签下方
                statusY = padding + rowHeight * 1.5f + padding * 0.5f;
            }

            // 确保状态行不会超出头部区域
            Rect statusRect = new Rect(
                padding,
                statusY,
                rect.width - padding * 2,
                rowHeight * 1.5f
            );

            string status = GetStatus(pawn);
            string type = "DD_Mech".Translate();
            Widgets.Label(statusRect, $"{type} | {status}");

            GUI.color = Color.white;
        }

        private void DrawGridPilot(Rect rect, CompMechPilotHolder pilotComp, float rowHeight)
        {
            float padding = rowHeight * 0.5f;
            
            Widgets.DrawBox(rect);
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.3f));

            // 标题
            Rect titleRect = new Rect(
                padding,
                rect.y + padding,
                rect.width - padding * 2,
                rowHeight + 5f
            );
            
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "DD_PilotTitle".Translate());
            Text.Font = GameFont.Small;
            
            // 内容
            Rect contentRect = new Rect(
                padding, 
                titleRect.yMax + padding * 0.5f, 
                rect.width - padding * 2, 
                rowHeight * 1.5f
            );
            
            if (pilotComp.HasPilots)
            {
                var pilots = pilotComp.GetPilots();
                List<string> pilotNames = new List<string>();
                foreach (var pilot in pilots)
                {
                    if (pilot != null) pilotNames.Add(pilot.LabelShort);
                }
                var pilotNamelist = string.Join(", ", pilotNames);
                Widgets.Label(contentRect, $"DD_PilotInfo".Translate(pilotNamelist));
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(contentRect, "DD_NoPilotShort".Translate());
                GUI.color = Color.white;
            }
        }
        
        private void DrawGridSkills(Rect rect, Pawn pawn, float rowHeight)
        {
            Widgets.DrawMenuSection(rect);
            
            float padding = rowHeight * 0.5f;
            
            // 标题
            Rect titleRect = new Rect(
                padding, 
                rect.y + padding, 
                rect.width - padding * 2,
                rowHeight + 5f
            );
            
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "Skills".Translate());
            Text.Font = GameFont.Small;
            
            // 技能列表区域
            Rect skillsArea = new Rect(
                0, 
                titleRect.yMax + padding, 
                rect.width, 
                rect.height - (titleRect.yMax - rect.y) - padding
            );
            
            if (pawn.skills == null || pawn.skills.skills.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(skillsArea.ContractedBy(padding * 2), "DD_MechNoSkill".Translate());
                GUI.color = Color.white;
                return;
            }
            
            // 滚动区域
            float skillHeight = rowHeight * 1.2f;
            float viewHeight = pawn.skills.skills.Count * skillHeight + padding * 2;
            
            Rect viewRect = new Rect(0, 0, skillsArea.width - 16f, viewHeight);
            Widgets.BeginScrollView(skillsArea, ref scrollPosition, viewRect);
            
            float curY = 0;
            foreach (var skill in pawn.skills.skills)
            {
                if (skill == null || skill.TotallyDisabled)
                    continue;
                    
                Rect skillRect = new Rect(
                    padding, 
                    curY, 
                    viewRect.width - padding * 2, 
                    skillHeight
                );
                
                DrawGridSkill(skillRect, skill, rowHeight);
                curY += skillHeight + padding * 0.3f;
            }
            
            Widgets.EndScrollView();
        }
        
        private void DrawGridSkill(Rect rect, SkillRecord skill, float rowHeight)
        {
            // 背景
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            
            // 布局：名称 | 进度条 | 等级
            float nameWidth = rect.width * 0.35f;
            float barWidth = rect.width * 0.45f;
            float levelWidth = rect.width * 0.2f;
            
            // 名称
            Rect nameRect = new Rect(rect.x, rect.y, nameWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, skill.def.LabelCap);
            
            // 进度条
            Rect barRect = new Rect(
                nameRect.xMax + rowHeight * 0.5f, 
                rect.y + (rect.height - 12f) / 2f, 
                barWidth - rowHeight, 
                12f
            );
            Widgets.FillableBar(barRect, skill.Level / 20f);
            
            // 等级
            Rect levelRect = new Rect(barRect.xMax + rowHeight * 0.5f, rect.y, levelWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(levelRect, $"Lv.{skill.Level}");
            
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 工具提示
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, 
                    $"<b>{skill.def.LabelCap}</b>\n" +
                    $"{skill.def.description}");
            }
        }
        
        private void DrawRenameButtons(Rect rect)
        {
            float buttonWidth = rect.width / 2 - 2.5f;
            
            Rect confirmRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(confirmRect, "OK".Translate()))
            {
                var pawn = SelPawn;
                if (pawn != null && !string.IsNullOrEmpty(nameBuffer))
                {
                    pawn.Name = new NameSingle(nameBuffer, false);
                    isRenaming = false;
                }
            }
            
            Rect cancelRect = new Rect(confirmRect.xMax + 5f, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                isRenaming = false;
            }
        }
        
        private string GetStatus(Pawn pawn)
        {
            if (pawn.Downed) return "Downed".Translate();
            if (pawn.Dead) return "Dead".Translate();
            if (pawn.Drafted) return "CommandDraftLabel".Translate();
            
            var pilotComp = pawn.TryGetComp<CompMechPilotHolder>();
            if (pilotComp == null || !pilotComp.HasPilots)
                return "DD_NoPilot".Translate();
                
            return "DD_Operational".Translate();
        }
        
        private void DrawError(string message)
        {
            Rect rect = new Rect(0, 0, size.x, size.y);
            Widgets.DrawMenuSection(rect);
            
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            GUI.color = Color.yellow;
            Widgets.Label(rect.ContractedBy(30f), message);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        public override bool IsVisible
        {
            get
            {
                var pawn = SelPawn;
                return pawn != null && pawn.TryGetComp<CompMechSkillInheritance>() != null;
            }
        }
    }
}
