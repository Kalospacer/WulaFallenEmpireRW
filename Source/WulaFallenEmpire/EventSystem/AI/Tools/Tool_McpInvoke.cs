using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire.EventSystem.AI.Mcp;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_McpInvoke : AITool
    {
        public override string Name => "mcp_invoke";
        public override string Description =>
            "调用某个 MCP server 的工具并返回结果文本。参数：server（server 名）、tool（工具名）、"
            + "arguments（JSON 对象）、timeout（可选秒数）。GABS 的游戏工具通过 games_call_tool 转发。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["server"] = SchemaString("MCP server 名。", nullable: false),
                ["tool"] = SchemaString("工具名。", nullable: false),
                ["arguments"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "传给工具的 JSON 参数对象。",
                    ["additionalProperties"] = true
                },
                ["timeout"] = SchemaInteger("超时秒数（可选）。", nullable: true)
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

                JObject mcpArgs = new JObject();
                if (TryGetObject(parsed, "arguments", out var argsDict) && argsDict != null)
                {
                    mcpArgs = JObject.FromObject(argsDict);
                }

                // The schema advertises `timeout`, so honour it rather than silently falling back to the
                // server's ToolTimeoutSec — a hung server would otherwise stall the loop for up to 120s
                // even when the model asked for a tight bound.
                if (TryGetInt(parsed, "timeout", out int timeoutSec) && timeoutSec > 0)
                {
                    return await McpConnectionManager.Instance
                        .InvokeAsync(server, tool, mcpArgs, timeoutSec, cancellationToken)
                        .ConfigureAwait(false);
                }
                return await McpConnectionManager.Instance.InvokeAsync(server, tool, mcpArgs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return "Error: mcp_invoke 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
