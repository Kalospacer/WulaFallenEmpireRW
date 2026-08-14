using System;
using System.Collections.Generic;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>
    /// 解析 SKILL.md 的 YAML frontmatter（最小子集：`---` 定界，顶层标量 +
    /// <c>dependencies.tools</c> 列表）。不引第三方 YAML 库。
    /// </summary>
    public static class SkillParser
    {
        public static bool TryParse(string contents, string sourcePath, out SkillMetadata metadata, out string error)
        {
            metadata = null;
            error = null;

            string frontmatter = ExtractFrontmatter(contents);
            if (frontmatter == null)
            {
                error = "missing YAML frontmatter delimited by ---";
                return false;
            }

            var lines = new List<string>(frontmatter.Replace("\r\n", "\n").Split('\n'));
            var meta = new SkillMetadata { SourcePath = sourcePath };

            // 找正文（frontmatter 之后）
            int endOfFrontmatter = contents.IndexOf("---", StringComparison.Ordinal);
            if (endOfFrontmatter >= 0)
            {
                int second = contents.IndexOf("---", endOfFrontmatter + 3, StringComparison.Ordinal);
                if (second >= 0)
                {
                    meta.Body = contents.Substring(second + 3).Trim();
                }
            }

            int toolsLine = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                int indent = CountIndent(line);
                if (indent > 0) continue; // 只看顶层

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                string value = Unquote(line.Substring(colon + 1).Trim());

                switch (key)
                {
                    case "name": meta.Name = value; break;
                    case "description": meta.Description = value; break;
                    case "short-description":
                    case "short_description": meta.ShortDescription = value; break;
                    case "dependencies": toolsLine = i; break;
                }
            }

            if (string.IsNullOrWhiteSpace(meta.Name))
            {
                error = "missing 'name' field";
                return false;
            }
            if (string.IsNullOrWhiteSpace(meta.Description))
            {
                error = "missing 'description' field";
                return false;
            }

            if (toolsLine >= 0)
            {
                meta.Dependencies = ParseToolDependencies(lines, toolsLine);
            }

            metadata = meta;
            return true;
        }

        private static string ExtractFrontmatter(string contents)
        {
            if (string.IsNullOrWhiteSpace(contents)) return null;
            string trimmed = contents.TrimStart('﻿', ' ', '\t', '\r', '\n');
            if (!trimmed.StartsWith("---", StringComparison.Ordinal)) return null;
            int first = trimmed.IndexOf("---", StringComparison.Ordinal);
            int second = trimmed.IndexOf("---", first + 3, StringComparison.Ordinal);
            if (second < 0) return null;
            return trimmed.Substring(first + 3, second - (first + 3));
        }

        private static List<SkillToolDependency> ParseToolDependencies(List<string> lines, int toolsLine)
        {
            var result = new List<SkillToolDependency>();
            int toolsIndent = CountIndent(lines[toolsLine]);

            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inItem = false;

            for (int i = toolsLine + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (inItem && current.Count > 0) { result.Add(Build(current)); current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); inItem = false; }
                    continue;
                }
                int indent = CountIndent(line);
                if (indent <= toolsIndent) break; // 退出 tools 块

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    if (inItem && current.Count > 0) result.Add(Build(current));
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    inItem = true;
                    string afterDash = trimmed.Substring(1).Trim();
                    if (afterDash.Length > 0) AddField(current, afterDash);
                }
                else if (inItem)
                {
                    AddField(current, trimmed);
                }
            }
            if (inItem && current.Count > 0) result.Add(Build(current));
            return result;
        }

        private static void AddField(Dictionary<string, string> fields, string kv)
        {
            int colon = kv.IndexOf(':');
            if (colon <= 0) return;
            string key = kv.Substring(0, colon).Trim();
            string value = kv.Substring(colon + 1).Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal) && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }
            fields[key] = value;
        }

        private static SkillToolDependency Build(Dictionary<string, string> fields)
        {
            string Get(string k) => fields.TryGetValue(k, out var v) ? v : null;
            return new SkillToolDependency
            {
                Type = Get("type"),
                Value = Get("value"),
                Description = Get("description"),
                Transport = Get("transport"),
                Command = Get("command"),
                Url = Get("url")
            };
        }

        /// <summary>
        /// Strips one layer of matching surrounding quotes from a frontmatter value. Without this a
        /// <c>name: "map-vision"</c> parsed to a name that still carried its quotes, so the skill index
        /// displayed the quoted form and lookups by the bare name failed even though the skill had loaded.
        /// </summary>
        private static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2) return value;
            char first = value[0];
            if (first != '"' && first != '\'') return value;
            return value[value.Length - 1] == first
                ? value.Substring(1, value.Length - 2)
                : value;
        }

        private static int CountIndent(string line)
        {
            int n = 0;
            foreach (char c in line)
            {
                if (c == ' ') n++;
                else if (c == '\t') n += 2;
                else break;
            }
            return n;
        }
    }
}
