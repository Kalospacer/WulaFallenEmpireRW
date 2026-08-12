using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimBridgeServer.Sdk;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Bridge
{
    /// <summary>
    /// RimBridgeServer 进程内 SDK 的软依赖门面。
    /// 未安装 RimBridgeServer 时，本类仍可被加载与调用（SDK 类型只出现在方法体内，
    /// 且入口先用反射探测），只是 IsAvailable 返回 false，两个工具给出可读错误。
    /// </summary>
    public static class RimBridgeFacade
    {
        private static bool? _cachedAvailable;

        /// <summary>RimBridgeServer 是否已安装且桥已就绪。</summary>
        public static bool IsAvailable
        {
            get
            {
                if (_cachedAvailable.HasValue)
                    return _cachedAvailable.Value;
                _cachedAvailable = Probe();
                return _cachedAvailable.Value;
            }
        }

        // 纯反射探测：不引用 SDK 类型，未装 SDK 时不会触发程序集加载异常。
        private static bool Probe()
        {
            try
            {
                var type = Type.GetType("RimBridgeServer.Sdk.RimBridge, RimBridgeServer.Sdk", false);
                if (type == null)
                    return false;
                var prop = type.GetProperty("IsReady", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                    return false;
                return prop.GetValue(null, null) is bool ready && ready;
            }
            catch (Exception ex)
            {
                Log.Warning("[Wula] RimBridge SDK 探测失败: " + ex.Message);
                return false;
            }
        }

        public static Task<string> ListToolsAsync(CancellationToken ct)
        {
            if (!IsAvailable)
            {
                return Task.FromResult("未检测到 RimBridgeServer（或其 SDK 未就绪）。"
                    + "游戏操作需要启用 RimBridgeServer 这个 mod。");
            }

            try
            {
                var tools = RimBridge.Current.Tools.List();
                var sb = new StringBuilder();
                sb.AppendLine($"共 {tools.Count} 个 RimBridge 工具：");
                foreach (var t in tools)
                {
                    sb.Append("- ").Append(t.Id);
                    if (t.Aliases != null && t.Aliases.Count > 0 && !string.Equals(t.Aliases[0], t.Id, StringComparison.Ordinal))
                    {
                        sb.Append(" (alias: ").Append(string.Join(", ", t.Aliases)).Append(')');
                    }
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(t.Title))
                        sb.Append("    ").Append(t.Title).AppendLine();
                    if (!string.IsNullOrWhiteSpace(t.Summary))
                        sb.Append("    ").Append(t.Summary).AppendLine();
                    if (!string.IsNullOrWhiteSpace(t.Category))
                        sb.Append("    分类: ").Append(t.Category).AppendLine();
                    if (t.Parameters != null && t.Parameters.Count > 0)
                    {
                        sb.Append("    参数: ");
                        bool first = true;
                        foreach (var p in t.Parameters)
                        {
                            if (!first) sb.Append(", ");
                            first = false;
                            sb.Append(p.Name).Append(" (").Append(p.ParameterType ?? "object").Append(p.Required ? ", 必填" : ", 可选").Append(')');
                        }
                        sb.AppendLine();
                    }
                }
                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult("Error: 列出 RimBridge 工具失败: " + ex.Message);
            }
        }

        public static async Task<string> CallAsync(string idOrAlias, object args, CancellationToken ct)
        {
            if (!IsAvailable)
            {
                return "未检测到 RimBridgeServer（或其 SDK 未就绪）。"
                    + "游戏操作需要启用 RimBridgeServer 这个 mod。";
            }

            try
            {
                var result = await RimBridge.Current.Tools.CallAsync(idOrAlias, args, null, ct).ConfigureAwait(false);
                if (result == null)
                    return "Error: RimBridge 返回了空结果。";

                if (result.Success)
                {
                    string body = result.Result == null
                        ? "null"
                        : JsonConvert.SerializeObject(result.Result, Formatting.Indented);
                    return "成功 (status=" + (result.Status ?? "ok") + ")\n" + body;
                }

                var err = result.Error;
                var sb = new StringBuilder();
                sb.Append("失败 (status=").Append(result.Status ?? "error").Append(')');
                if (err != null)
                {
                    if (!string.IsNullOrWhiteSpace(err.Code))
                        sb.Append("\ncode: ").Append(err.Code);
                    if (!string.IsNullOrWhiteSpace(err.Message))
                        sb.Append("\nmessage: ").Append(err.Message);
                    if (!string.IsNullOrWhiteSpace(err.ExceptionType))
                        sb.Append("\nexceptionType: ").Append(err.ExceptionType);
                    if (err.Details != null)
                        sb.Append("\ndetails: ").Append(JsonConvert.SerializeObject(err.Details));
                }
                return sb.ToString();
            }
            catch (OperationCanceledException)
            {
                return "Error: bridge_call 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: bridge_call 调用失败: " + ex.Message;
            }
        }
    }
}
