using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Bridge;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 进程内调用 RimBridgeServer 的一个游戏工具（如 rimworld/click_cell、rimworld/take_screenshot）。
    /// </summary>
    public class Tool_BridgeCall : AITool
    {
        public override string Name => "bridge_call";
        public override string Description =>
            "进程内调用 RimBridgeServer 的一个游戏操作工具并返回结果。参数：tool（工具 id 或别名，"
            + "来自 bridge_list_tools）、arguments（JSON 对象，按 bridge_list_tools 返回的参数填）、"
            + "timeout（可选秒数）。只读工具优先；改动类（点击/存档/推进时间）确认后再做。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["tool"] = SchemaString("工具 id 或别名（bridge_list_tools 返回的）。", nullable: false),
                ["arguments"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "传给工具的 JSON 参数对象。",
                    ["additionalProperties"] = true
                },
                ["timeout"] = SchemaInteger("超时秒数（可选）。", nullable: true)
            };
            return SchemaObject(properties, RequiredList("tool"));
        }

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parsed = ParseJsonArgs(args);
                if (!TryGetString(parsed, "tool", out var tool) || string.IsNullOrWhiteSpace(tool))
                    return "Error: 缺少 tool。";

                Dictionary<string, object> callArgs = null;
                if (TryGetObject(parsed, "arguments", out var argsDict) && argsDict != null)
                {
                    callArgs = argsDict;
                }

                if (TryGetInt(parsed, "timeout", out var timeoutSec) && timeoutSec > 0)
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                        return await RimBridgeFacade.CallAsync(tool, callArgs, cts.Token).ConfigureAwait(false);
                    }
                }

                return await RimBridgeFacade.CallAsync(tool, callArgs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return "Error: bridge_call 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
