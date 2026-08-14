using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Skills
{
    /// <summary>
    /// 扫描 skill root 下的 SKILL.md，缓存解析结果，坏文件记错误不崩。
    /// </summary>
    public sealed class SkillLoader
    {
        private List<SkillMetadata> _skills = new List<SkillMetadata>();
        private List<string> _errors = new List<string>();
        private bool _loaded;

        public IReadOnlyList<SkillMetadata> Skills => _skills;
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>扫描目录。空/缺失目录返回空，不抛。</summary>
        public void Load(IEnumerable<string> roots)
        {
            if (_loaded) return;
            _loaded = true;

            if (roots == null) return;
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                ScanDirectory(root);
            }
        }

        private void ScanDirectory(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (var file in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
                {
                    try
                    {
                        string contents = File.ReadAllText(file);
                        if (SkillParser.TryParse(contents, file, out var meta, out var error))
                        {
                            _skills.Add(meta);
                        }
                        else
                        {
                            _errors.Add($"{file}: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _errors.Add($"{file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _errors.Add($"扫描 skill 目录 {root} 失败: {ex.Message}");
            }
        }

        public SkillMetadata GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
