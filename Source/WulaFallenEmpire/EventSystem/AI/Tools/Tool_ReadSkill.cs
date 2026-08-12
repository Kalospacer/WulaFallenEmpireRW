using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Skills;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 渐进式披露：平时只把 skill 的 name+description 注入 system prompt，
    /// 模型需要完整操作手册时用本工具加载 SKILL.md 全文。
    /// </summary>
    public class Tool_ReadSkill : AITool
    {
        public override string Name => "read_skill";
        public override string Description =>
            "加载某个 skill 的完整操作手册（SKILL.md 全文）。"
            + "当 system prompt 的 AVAILABLE SKILLS 里列出的 skill 与当前任务相关时，先读它再动手。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["name"] = SchemaString("skill 名（AVAILABLE SKILLS 列表里 - 后面的名字）。", nullable: false)
            };
            return SchemaObject(properties, RequiredList("name"));
        }

        public override Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parsed = ParseJsonArgs(args);
                if (!TryGetString(parsed, "name", out var name) || string.IsNullOrWhiteSpace(name))
                {
                    return Task.FromResult("Error: 缺少 skill 名。可用的 skill 见 system prompt 的 AVAILABLE SKILLS 段。");
                }
                string body = SkillSystem.GetBody(name);
                if (string.IsNullOrWhiteSpace(body))
                {
                    return Task.FromResult($"未找到 skill '{name}'。" + SkillSystem.GetIndexText());
                }
                return Task.FromResult(body);
            }
            catch (Exception ex)
            {
                return Task.FromResult("Error: " + ex.Message);
            }
        }
    }
}
