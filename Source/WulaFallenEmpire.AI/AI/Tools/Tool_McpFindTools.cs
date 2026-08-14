using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_McpFindTools : AITool
    {
        public override string Name => "mcp_find_tools";
        public override string Description =>
            "列出所有已配置的外部 MCP server（如 GABS）暴露的工具，按 server 分组。"
            + "GABS 的游戏工具（rimworld_* 等）需先用此工具发现名字，"
            + "再通过 mcp_tool_detail 看参数、mcp_invoke 调用。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["server"] = SchemaString("可选：只看某个 server 的工具。", nullable: true)
            };
            return SchemaObject(properties, null);
        }

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string result = await McpConnectionManager.Instance.FindAllToolsAsync(cancellationToken).ConfigureAwait(false);
                return result ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                return "Error: mcp_find_tools 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
