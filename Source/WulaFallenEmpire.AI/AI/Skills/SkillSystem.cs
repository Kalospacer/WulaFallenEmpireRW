using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>
    /// skill 系统门面：惰性加载、渐进式披露索引、缺依赖检查。供 system prompt 注入与工具使用。
    /// </summary>
    public static class SkillSystem
    {
        private static SkillLoader _loader;
        private static readonly object LoadLock = new object();
        private static bool _notifiedMissing;

        public static IReadOnlyList<SkillMetadata> Skills
        {
            get { EnsureLoaded(); return _loader.Skills; }
        }

        public static IReadOnlyList<string> Errors
        {
            get { EnsureLoaded(); return _loader.Errors; }
        }

        /// <summary>渐进式披露索引（name + description），空 = 无 skill。</summary>
        public static string GetIndexText()
        {
            EnsureLoaded();
            return SkillPromptBuilder.BuildIndexText(_loader.Skills);
        }

        public static string GetBody(string name)
        {
            EnsureLoaded();
            var skill = _loader.GetByName(name);
            return SkillPromptBuilder.BuildFullBody(skill);
        }

        public static List<MissingMcpDependency> CheckMissing()
        {
            EnsureLoaded();
            McpConnectionManager.Instance.ReloadConfig();
            return SkillDependencyResolver.CheckMissing(_loader.Skills, McpConnectionManager.Instance.Configs);
        }

        /// <summary>首次加载时，若有缺 MCP 依赖的 skill，弹一条非阻塞提示。</summary>
        public static void NotifyMissingDependenciesOnce()
        {
            if (_notifiedMissing) return;
            _notifiedMissing = true;
            try
            {
                var missing = CheckMissing();
                if (missing == null || missing.Count == 0) return;
                var names = string.Join(", ", missing.Select(m => m.SkillName).Distinct());
                Messages.Message($"WulaAI: 有 skill 缺少 MCP 依赖 ({names})，在 Mod 设置里补全 mcpServersJson 后可用。", MessageTypeDefOf.NeutralEvent);
            }
            catch
            {
                // 提示失败不影响主流程
            }
        }

        private static void EnsureLoaded()
        {
            if (_loader != null) return;
            lock (LoadLock)
            {
                if (_loader != null) return;
                var loader = new SkillLoader();
                loader.Load(GetRoots());
                _loader = loader;
            }
        }

        private static IEnumerable<string> GetRoots()
        {
            var roots = new List<string>();
            var settings = WulaFallenEmpireAIMod.settings;
            if (settings != null && !string.IsNullOrWhiteSpace(settings.skillsDirectory))
            {
                roots.Add(settings.skillsDirectory);
            }

            // mod 目录下的 Skills/
            try
            {
                var mod = LoadedModManager.GetMod(typeof(WulaFallenEmpireAIMod));
                string rootDir = mod?.Content?.RootDir;
                if (!string.IsNullOrWhiteSpace(rootDir))
                {
                    roots.Add(Path.Combine(rootDir, "Skills"));
                }
            }
            catch
            {
                // 找不到 mod root 就跳过默认目录
            }
            return roots;
        }

        /// <summary>测试/重载用：清空缓存，下次访问重新扫描。</summary>
        public static void Reset()
        {
            lock (LoadLock)
            {
                _loader = null;
                _notifiedMissing = false;
            }
        }
    }
}
