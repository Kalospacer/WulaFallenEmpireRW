using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_McpToolDetail : AITool
    {
        public override string Name => "mcp_tool_detail";
        public override string Description =>
            "查看某个 MCP server 的单个工具的参数 schema 与描述。先用 mcp_find_tools 发现工具名。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["server"] = SchemaString("MCP server 名。", nullable: false),
                ["tool"] = SchemaString("工具名。", nullable: false)
            };
            return SchemaObject(properties, RequiredList("server", "tool"));
        }

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parsed = ParseJsonArgs(args);
                if (!TryGetString(parsed, "server", out var server) || string.IsNullOrWhiteSpace(server))
                    return "Error: 缺少 server。";
                if (!TryGetString(parsed, "tool", out var tool) || string.IsNullOrWhiteSpace(tool))
                    return "Error: 缺少 tool。";

                return await McpConnectionManager.Instance.DescribeToolAsync(server, tool, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return "Error: mcp_tool_detail 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
