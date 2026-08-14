using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>
    /// 多 MCP server 的连接管理：从 ModSettings 的 <c>mcpServersJson</c> 读配置，
    /// 按需惰性创建 <see cref="McpClient"/>，server 名作为工具命名空间。
    /// </summary>
    public sealed class McpConnectionManager : IDisposable
    {
        private static McpConnectionManager _instance;

        private readonly ConcurrentDictionary<string, McpClient> _clients =
            new ConcurrentDictionary<string, McpClient>(StringComparer.OrdinalIgnoreCase);
        private List<McpServerConfig> _configs = new List<McpServerConfig>();
        private List<string> _configErrors = new List<string>();
        private int _configLoaded;

        public static McpConnectionManager Instance => _instance ?? (_instance = new McpConnectionManager());

        public IReadOnlyList<McpServerConfig> Configs => _configs;
        public IReadOnlyList<string> ConfigErrors => _configErrors;
        public bool HasServers => _configs != null && _configs.Count > 0;

        private McpConnectionManager() { }

        static McpConnectionManager()
        {
            // RimWorld 退出时杀掉已 spawn 的 MCP 子进程，防孤儿 gabs.exe/python。
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { _instance?.Dispose(); }
                catch { }
            };
        }

        public void ReloadConfig()
        {
            string json = WulaFallenEmpireAIMod.settings?.mcpServersJson;
            if (string.IsNullOrWhiteSpace(json))
            {
                json = McpServerConfig.DefaultJson();
            }

            var parsed = McpServerConfig.ParseMany(json, out var errors);
            _configs = parsed.Where(c => c.Enabled).ToList();
            _configErrors = errors ?? new List<string>();
            Interlocked.Exchange(ref _configLoaded, 1);

            // Drop clients whose server disappeared from the config, and also those whose connection
            // details changed: keying only on the name kept a client bound to the previous process or
            // endpoint, so editing a command or url in Mod Settings had no effect until a game restart.
            // GroupBy (not ToDictionary) because mcpServersJson is user-authored and may repeat a name.
            var byName = _configs
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _clients)
            {
                bool stale = !byName.TryGetValue(kv.Key, out var newCfg)
                    || !newCfg.HasSameConnection(kv.Value.Config);
                if (!stale) continue;
                if (_clients.TryRemove(kv.Key, out var client))
                {
                    client.Dispose();
                }
            }
        }

        private void EnsureConfigLoaded()
        {
            if (Interlocked.CompareExchange(ref _configLoaded, 0, 0) == 0)
            {
                ReloadConfig();
            }
        }

        public McpClient GetClient(string serverName)
        {
            EnsureConfigLoaded();
            if (string.IsNullOrWhiteSpace(serverName)) return null;

            var cfg = _configs.FirstOrDefault(c => string.Equals(c.Name, serverName, StringComparison.OrdinalIgnoreCase));
            if (cfg == null) return null;

            return _clients.GetOrAdd(cfg.Name, _ => new McpClient(cfg));
        }

        public async Task<string> FindAllToolsAsync(CancellationToken ct)
        {
            EnsureConfigLoaded();
            if (_configs.Count == 0)
            {
                return "未配置任何 MCP server。在 Mod 设置中填写 mcpServersJson。";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var cfg in _configs)
            {
                var client = GetClient(cfg.Name);
                if (client == null) continue;
                sb.Append("## server: ").Append(cfg.Name);
                if (!string.IsNullOrWhiteSpace(cfg.Url)) sb.Append(" (").Append(cfg.Url).Append(')');
                sb.AppendLine();
                try
                {
                    var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);
                    if (tools.Count == 0)
                    {
                        sb.AppendLine("  (无工具)");
                    }
                    foreach (var t in tools)
                    {
                        string desc = string.IsNullOrWhiteSpace(t.Description)
                            ? ""
                            : " — " + t.Description.Replace('\n', ' ').Replace('\r', ' ');
                        sb.Append("  - ").Append(t.Name).Append(desc).AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    sb.Append("  (不可用: ").Append(ex.Message).Append(')').AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public async Task<string> DescribeToolAsync(string serverName, string toolName, CancellationToken ct)
        {
            EnsureConfigLoaded();
            var client = GetClient(serverName);
            if (client == null)
            {
                return $"Error: 未找到 MCP server '{serverName}'。";
            }
            try
            {
                var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);
                var tool = tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
                if (tool == null)
                {
                    return $"Error: server '{serverName}' 没有工具 '{toolName}'。";
                }
                string schema = tool.InputSchema != null ? tool.InputSchema.ToString(Newtonsoft.Json.Formatting.None) : "{}";
                return $"工具 {tool.Name}\n描述: {tool.Description}\n只读: {tool.ReadOnly}\ninputSchema: {schema}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public Task<string> InvokeAsync(string serverName, string toolName, JObject arguments, CancellationToken ct)
        {
            return InvokeAsync(serverName, toolName, arguments, null, ct);
        }

        /// <summary>
        /// Invokes an MCP tool, optionally overriding the server's configured tool timeout.
        /// </summary>
        /// <param name="timeoutSecOverride">
        /// Caller-supplied timeout in seconds; null uses the server config's <c>ToolTimeoutSec</c>.
        /// Clamped to the same 2..600 range as the configured value.
        /// </param>
        public async Task<string> InvokeAsync(string serverName, string toolName, JObject arguments, int? timeoutSecOverride, CancellationToken ct)
        {
            EnsureConfigLoaded();
            var client = GetClient(serverName);
            if (client == null)
            {
                return $"Error: 未找到 MCP server '{serverName}'。";
            }

            int requested = timeoutSecOverride ?? ResolveTimeoutSec(serverName);
            int timeoutSec = Math.Max(2, Math.Min(600, requested));
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                var result = await client.CallToolAsync(toolName, arguments, cts.Token).ConfigureAwait(false);
                if (!result.Success)
                {
                    return result.Error ?? $"Error: 工具 '{toolName}' 调用失败。";
                }
                return result.Text;
            }
        }

        private int ResolveTimeoutSec(string serverName)
        {
            var cfg = _configs.FirstOrDefault(c => string.Equals(c.Name, serverName, StringComparison.OrdinalIgnoreCase));
            return cfg?.ToolTimeoutSec ?? 120;
        }

        public async Task StopAllAsync()
        {
            foreach (var kv in _clients)
            {
                try { await kv.Value.StopAsync().ConfigureAwait(false); }
                catch { }
            }
            _clients.Clear();
        }

        /// <summary>连接测试：逐个 server 尝试 tools/list，返回可读结果。供设置界面按钮使用。</summary>
        public async Task<string> TestConnectionsAsync()
        {
            ReloadConfig();
            if (_configs.Count == 0)
            {
                return "未解析出任何启用的 MCP server。请检查 mcpServersJson。";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var cfg in _configs)
            {
                var client = GetClient(cfg.Name);
                if (client == null)
                {
                    sb.Append(cfg.Name).AppendLine(": 配置无效");
                    continue;
                }
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, cfg.StartupTimeoutSec))))
                    {
                        var tools = await client.ListToolsAsync(cts.Token).ConfigureAwait(false);
                        sb.Append(cfg.Name).Append(": OK (").Append(tools.Count).AppendLine(" 工具)");
                    }
                }
                catch (Exception ex)
                {
                    sb.Append(cfg.Name).Append(": 失败 — ").AppendLine(ex.Message);
                }
            }
            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
            foreach (var kv in _clients)
            {
                kv.Value.Dispose();
            }
            _clients.Clear();
        }
    }
}
