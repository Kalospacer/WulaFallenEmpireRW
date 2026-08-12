using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>
    /// stdio 传输：spawn 子进程，stdin/stdout 逐行 JSON-RPC（换行分帧、禁内嵌换行），
    /// stderr 仅日志。关 stdin = 优雅关闭信号，超时则 Kill。
    /// </summary>
    public sealed class StdioMcpTransport : IMcpTransport
    {
        private readonly McpServerConfig _config;
        private Process _process;
        private StreamWriter _writer;
        private CancellationTokenSource _readCts;
        private Task _readerTask;
        private int _disposed;

        public StdioMcpTransport(McpServerConfig config)
        {
            _config = config;
        }

        public bool IsReady => _process != null && !_process.HasExited;

        private static string QuoteArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOf('"') >= 0) arg = arg.Replace("\"", "\\\"");
            if (arg.IndexOf(' ') >= 0 || arg.IndexOf('\t') >= 0) return "\"" + arg + "\"";
            return arg;
        }

        public event Action<string> OnLineReceived;
        public event Action<string> OnDisconnected;

        public Task StartAsync(CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(_config.Cwd) ? null : _config.Cwd
            };

            foreach (var arg in _config.Args ?? new System.Collections.Generic.List<string>())
            {
                // net472 没有 ProcessStartInfo.ArgumentList，手动拼参数字符串（带引号）。
                psi.Arguments += QuoteArg(arg) + " ";
            }

            if (_config.Env != null)
            {
                foreach (var kv in _config.Env)
                {
                    psi.Environment[kv.Key] = kv.Value;
                }
            }

            try
            {
                _process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法启动 MCP server '{_config.Name}' ({_config.Command}): {ex.Message}", ex);
            }

            _writer = _process.StandardInput;
            _readCts = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReaderLoop(_process, _readCts.Token));
            return Task.FromResult(true);
        }

        private async Task ReaderLoop(Process process, CancellationToken ct)
        {
            try
            {
                var reader = process.StandardOutput;
                while (!ct.IsCancellationRequested)
                {
                    // 逐行读取；ReadLineAsync 需要真实的异步，这里用 Task.Run 包裹同步
                    // ReadLine 以规避 net472 下 StreamReader 与取消的耦合问题。
                    string line = await ReadLineAsync(reader, ct).ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    RaiseLine(line);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    WulaLog.Debug($"[WulaAI][MCP][{_config.Name}] stdio reader stopped: {ex.Message}");
                }
            }
            finally
            {
                if (!ct.IsCancellationRequested && !process.HasExited)
                {
                    RaiseDisconnected("stdio 进程提前退出");
                }
            }
        }

        private static Task<string> ReadLineAsync(StreamReader reader, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return reader.ReadLine();
            }, ct);
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            var writer = _writer;
            if (writer == null) throw new InvalidOperationException($"MCP server '{_config.Name}' 未启动。");
            await Task.Run(() => writer.WriteLine(line), ct).ConfigureAwait(false);
            await Task.Run(() => writer.Flush(), ct).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            var process = _process;
            _process = null;
            if (process == null) return;

            try
            {
                if (_readCts != null)
                {
                    _readCts.Cancel();
                }
                // 关 stdin = 优雅关闭信号
                try { _writer?.Close(); } catch { }
                _writer = null;

                if (!process.HasExited && !process.WaitForExit(3000))
                {
                    process.Kill();
                }
                if (_readerTask != null)
                {
                    try { await _readerTask.ConfigureAwait(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI][MCP][{_config.Name}] stop error: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private void RaiseLine(string line)
        {
            OnLineReceived?.Invoke(line);
        }

        private void RaiseDisconnected(string reason)
        {
            OnDisconnected?.Invoke(reason);
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _readCts?.Cancel();
            _readCts?.Dispose();
            try { _process?.Kill(); } catch { }
            _process?.Dispose();
        }
    }
}
