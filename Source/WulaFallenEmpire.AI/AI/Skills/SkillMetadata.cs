using System.Collections.Generic;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>skill 声明的 MCP 工具依赖（对齐 Codex <c>SkillToolDependency</c>）。</summary>
    public sealed class SkillToolDependency
    {
        public string Type;
        public string Value;
        public string Description;
        public string Transport;
        public string Command;
        public string Url;
    }

    /// <summary>一个 SKILL.md 的元数据（对齐 Codex <c>SkillMetadata</c>）。</summary>
    public sealed class SkillMetadata
    {
        public string Name;
        public string Description;
        public string ShortDescription;
        public List<SkillToolDependency> Dependencies = new List<SkillToolDependency>();
        /// <summary>SKILL.md 文件路径。</summary>
        public string SourcePath;
        /// <summary>frontmatter 之后的正文（渐进式披露时用到才加载）。</summary>
        public string Body;
    }
}
