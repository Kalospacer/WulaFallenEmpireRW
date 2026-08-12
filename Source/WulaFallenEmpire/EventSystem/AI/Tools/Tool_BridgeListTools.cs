using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Bridge;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 列出 RimBridgeServer 暴露的游戏操作工具（rimworld_*/rimbridge_*，进程内直调）。
    /// </summary>
    public class Tool_BridgeListTools : AITool
    {
        public override string Name => "bridge_list_tools";
        public override string Description =>
            "列出 RimBridgeServer（游戏内桥）暴露的全部游戏操作工具（rimworld/*、rimbridge/*）。"
            + "这是 Wula 操作 RimWorld（镜头/UI/点击/存档/截图/推进时间等）的唯一入口："
            + "先用本工具发现工具名和参数，再用 bridge_call 调用。只读，不改变游戏状态。";

        public override Dictionary<string, object> GetParametersSchema()
        {
            return SchemaObject(new Dictionary<string, object>(), null);
        }

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RimBridgeFacade.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return "Error: bridge_list_tools 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
