using System;
using System.Threading;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>
    /// MCP 传输抽象。每条完整 JSON-RPC 消息一行；传输层负责换行分帧和
    /// 进程/HTTP 生命周期，JSON-RPC 封包与 id 关联由 <see cref="McpClient"/> 负责。
    /// </summary>
    public interface IMcpTransport : IDisposable
    {
        bool IsReady { get; }

        /// <summary>启动（spawn 子进程或探测 HTTP 端点）。</summary>
        Task StartAsync(CancellationToken ct);

        /// <summary>发送一条不含换行的 JSON-RPC 消息。</summary>
        Task SendAsync(string line, CancellationToken ct);

        /// <summary>收到一条完整消息（响应或通知）。</summary>
        event Action<string> OnLineReceived;

        /// <summary>意外断开，参数为原因。</summary>
        event Action<string> OnDisconnected;

        Task StopAsync();
    }
}
