using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>一个缺依赖的 skill → 待安装 MCP server 的对应关系。</summary>
    public sealed class MissingMcpDependency
    {
        public string SkillName;
        public SkillToolDependency Dependency;
        public string SuggestedServerJson;
    }

    /// <summary>
    /// 匹配 skill 的 MCP 依赖与已配置 server，产出缺失项与建议配置。
    /// canonical key 对齐 Codex：<c>mcp__{transport}__{identifier}</c>。
    /// </summary>
    public static class SkillDependencyResolver
    {
        public static List<MissingMcpDependency> CheckMissing(
            IReadOnlyList<SkillMetadata> skills,
            IReadOnlyList<McpServerConfig> configs)
        {
            var result = new List<MissingMcpDependency>();
            if (skills == null || skills.Count == 0) return result;

            foreach (var skill in skills)
            {
                foreach (var dep in skill.Dependencies ?? new List<SkillToolDependency>())
                {
                    if (dep == null) continue;
                    if (!string.Equals(dep.Type, "mcp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsResolved(dep, configs)) continue;

                    result.Add(new MissingMcpDependency
                    {
                        SkillName = skill.Name,
                        Dependency = dep,
                        SuggestedServerJson = BuildSuggestedServerJson(dep)
                    });
                }
            }
            return result;
        }

        private static bool IsResolved(SkillToolDependency dep, IReadOnlyList<McpServerConfig> configs)
        {
            if (configs == null) return false;

            string value = (dep.Value ?? string.Empty).Trim();
            string command = (dep.Command ?? string.Empty).Trim();
            string url = (dep.Url ?? string.Empty).Trim();

            foreach (var cfg in configs)
            {
                if (cfg == null) continue;
                // 按 canonical key 精确匹配
                if (!string.IsNullOrEmpty(value) && string.Equals(value, cfg.CanonicalKey, StringComparison.OrdinalIgnoreCase)) return true;
                // 按 command/url/name 宽松匹配
                if (!string.IsNullOrEmpty(command) && string.Equals(command, cfg.Command ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(url) && string.Equals(url, cfg.Url ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string BuildSuggestedServerJson(SkillToolDependency dep)
        {
            string name = "skill-" + SanitizeName((dep.Value ?? dep.Command ?? "mcp").Trim());
            bool isHttp = string.Equals(dep.Transport, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dep.Transport, "streamable_http", StringComparison.OrdinalIgnoreCase);

            var obj = new JObject
            {
                ["name"] = name,
                ["enabled"] = true
            };
            if (isHttp)
            {
                obj["transport"] = "http";
                obj["url"] = dep.Url ?? string.Empty;
            }
            else
            {
                obj["transport"] = "stdio";
                obj["command"] = dep.Command ?? string.Empty;
                obj["args"] = new JArray();
            }
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "mcp";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? "mcp" : sb.ToString();
        }
    }
}
