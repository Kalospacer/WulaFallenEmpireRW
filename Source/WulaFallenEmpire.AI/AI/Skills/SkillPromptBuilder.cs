using System.Collections.Generic;
using System.Text;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>
    /// 渐进式披露：平时只把 skill 的 name + description 注入 prompt，用到才加载正文。
    /// </summary>
    public static class SkillPromptBuilder
    {
        public static string BuildIndexText(IReadOnlyList<SkillMetadata> skills)
        {
            if (skills == null || skills.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("# AVAILABLE SKILLS");
            sb.AppendLine("These skills describe how to use specific tools/servers. Reference them by name when relevant.");
            foreach (var s in skills)
            {
                string desc = string.IsNullOrWhiteSpace(s.Description) ? "" : " — " + s.Description;
                sb.Append("- ").Append(s.Name).Append(desc).AppendLine();
            }
            return sb.ToString();
        }

        public static string BuildFullBody(SkillMetadata skill)
        {
            if (skill == null) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine($"# SKILL: {skill.Name}");
            sb.AppendLine(skill.Description);
            if (!string.IsNullOrWhiteSpace(skill.Body))
            {
                sb.AppendLine();
                sb.Append(skill.Body);
            }
            return sb.ToString();
        }
    }
}
