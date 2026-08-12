using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>单个 MCP 工具的描述（来自 tools/list）。</summary>
    public sealed class McpToolDescriptor
    {
        public string Name;
        public string Description;
        public JObject InputSchema;
        public bool ReadOnly;
    }

    /// <summary>tools/call 的结果，内容已展平为文本。</summary>
    public sealed class McpCallResult
    {
        public bool Success;
        public string Text;
        public JObject Raw;
        public string Error;

        public static McpCallResult FromError(string error)
        {
            return new McpCallResult { Success = false, Error = error, Text = error };
        }
    }

    /// <summary>
    /// dual-era MCP 客户端：启动后用 <c>server/discover</c> 探测时代，modern（2026-07-28+）
    /// 走每请求 <c>_meta</c>，legacy（2025-11-25 及以前，如 GABS）走 <c>initialize</c> 握手。
    /// 工具命名空间 = server name（照抄 Codex），调用时原始 tool name 回传。
    /// </summary>
    public sealed class McpClient : IDisposable
    {
        private const string ModernVersion = "2026-07-28";
        private const string LegacyVersion = "2024-11-05";
        private const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";
        private const string MetaClientInfo = "io.modelcontextprotocol/clientInfo";
        private const string MetaClientCapabilities = "io.modelcontextprotocol/clientCapabilities";

        private enum Era { Unknown, Legacy, Modern }

        private readonly McpServerConfig _config;
        private IMcpTransport _transport;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JObject>> _pending =
            new ConcurrentDictionary<long, TaskCompletionSource<JObject>>();
        private long _nextId;
        private bool _started;
        private Era _era = Era.Unknown;
        private string _protocolVersion;
        private int _disposed;

        public string ServerName => _config?.Name ?? string.Empty;
        public bool IsStarted => _started;

        public McpClient(McpServerConfig config)
        {
            _config = config;
        }

        private IMcpTransport CreateTransport()
        {
            if (_config.IsHttp) return new HttpMcpTransport(_config);
            return new StdioMcpTransport(_config);
        }

        private async Task EnsureStartedAsync(CancellationToken ct)
        {
            if (_started) return;
            if (_transport == null)
            {
                _transport = CreateTransport();
                _transport.OnLineReceived += OnLineReceived;
                _transport.OnDisconnected += OnDisconnected;
            }
            await _transport.StartAsync(ct).ConfigureAwait(false);
            await DetectEraAsync(ct).ConfigureAwait(false);
            _started = true;
        }

        private void OnLineReceived(string line)
        {
            JObject msg;
            try { msg = JObject.Parse(line); }
            catch { return; }

            var idToken = msg["id"];
            if (idToken == null || idToken.Type == JTokenType.Null)
            {
                // 通知，忽略（首版不消费 notifications/*）
                return;
            }
            if (!long.TryParse(idToken.Value<object>()?.ToString(), out long id)) return;
            if (_pending.TryRemove(id, out var tcs))
            {
                tcs.TrySetResult(msg);
            }
        }

        private void OnDisconnected(string reason)
        {
            // 断开后未决请求全部失败，触发上层重试/重启。
            foreach (var kv in _pending)
            {
                if (_pending.TryRemove(kv.Key, out var tcs))
                {
                    tcs.TrySetException(new InvalidOperationException($"MCP server '{ServerName}' 断开: {reason}"));
                }
            }
            _started = false;
        }

        private async Task DetectEraAsync(CancellationToken ct)
        {
            // 先按 modern 探测 server/discover
            var probe = await SendRequestAsync("server/discover", WithMeta(new JObject(), ModernVersion), ct).ConfigureAwait(false);

            if (IsUnsupportedVersion(probe))
            {
                _era = Era.Modern;
                _protocolVersion = PickSupportedVersion(probe["error"]?["data"] as JObject);
            }
            else if (probe["result"]?["supportedVersions"] is JArray)
            {
                _era = Era.Modern;
                _protocolVersion = PickSupportedVersion(probe["result"] as JObject);
            }
            else
            {
                _era = Era.Legacy;
                _protocolVersion = LegacyVersion;
                await InitializeLegacyAsync(ct).ConfigureAwait(false);
            }
        }

        private static bool IsUnsupportedVersion(JObject resp)
        {
            return resp["error"]?["code"]?.Value<int>() == -32022;
        }

        private static string PickSupportedVersion(JObject data)
        {
            if (data == null) return ModernVersion;
            var supported = data["supported"] as JArray ?? data["supportedVersions"] as JArray;
            if (supported != null)
            {
                foreach (var v in supported)
                {
                    string s = v.Value<string>();
                    if (s == ModernVersion) return ModernVersion;
                }
                var first = supported.First?.Value<string>();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            return ModernVersion;
        }

        private async Task InitializeLegacyAsync(CancellationToken ct)
        {
            var initParams = new JObject
            {
                ["protocolVersion"] = LegacyVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "WulaAI", ["version"] = "1.0.0" }
            };
            await SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);
            // 发完 initialize 后必须补 initialized 通知
            await SendNotificationAsync("notifications/initialized", ct).ConfigureAwait(false);
        }

        private Task SendNotificationAsync(string method, CancellationToken ct)
        {
            var msg = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };
            return _transport.SendAsync(msg.ToString(Formatting.None), ct);
        }

        private async Task<JObject> SendRequestAsync(string method, JObject methodParams, CancellationToken ct)
        {
            long id = Interlocked.Increment(ref _nextId);
            var req = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method
            };
            if (methodParams != null) req["params"] = methodParams;

            var tcs = new TaskCompletionSource<JObject>();
            _pending[id] = tcs;

            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    if (_pending.TryRemove(id, out var t))
                    {
                        t.TrySetCanceled();
                    }
                });
            }

            try
            {
                await _transport.SendAsync(req.ToString(Formatting.None), ct).ConfigureAwait(false);
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
                _pending.TryRemove(id, out _);
            }
        }

        private async Task<JObject> RequestAsync(string method, JObject methodParams, CancellationToken ct)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            var param = methodParams ?? new JObject();
            if (_era == Era.Modern)
            {
                param = WithMeta(param, _protocolVersion);
            }
            return await SendRequestAsync(method, param, ct).ConfigureAwait(false);
        }

        private static JObject WithMeta(JObject methodParams, string version)
        {
            var p = methodParams?.DeepClone() as JObject ?? new JObject();
            p["_meta"] = new JObject
            {
                [MetaProtocolVersion] = version,
                [MetaClientInfo] = new JObject { ["name"] = "WulaAI", ["version"] = "1.0.0" },
                [MetaClientCapabilities] = new JObject()
            };
            return p;
        }

        public async Task<List<McpToolDescriptor>> ListToolsAsync(CancellationToken ct)
        {
            var resp = await RequestAsync("tools/list", new JObject(), ct).ConfigureAwait(false);
            var result = new List<McpToolDescriptor>();
            var tools = resp["result"]?["tools"] as JArray;
            if (tools == null) return result;

            foreach (var t in tools)
            {
                var obj = t as JObject;
                if (obj == null) continue;
                string name = obj["name"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!_config.IsToolAllowed(name)) continue;

                var annotations = obj["annotations"] as JObject;
                bool readOnly = annotations?["readOnlyHint"]?.Value<bool>()
                    ?? annotations?["read_only_hint"]?.Value<bool>()
                    ?? false;

                result.Add(new McpToolDescriptor
                {
                    Name = name,
                    Description = obj["description"]?.Value<string>() ?? string.Empty,
                    InputSchema = obj["inputSchema"] as JObject,
                    ReadOnly = readOnly
                });
            }
            return result;
        }

        public async Task<McpCallResult> CallToolAsync(string name, JObject arguments, CancellationToken ct)
        {
            var param = new JObject
            {
                ["name"] = name,
                ["arguments"] = arguments ?? new JObject()
            };
            JObject resp;
            try
            {
                resp = await RequestAsync("tools/call", param, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return McpCallResult.FromError("MCP 工具调用被取消或超时。");
            }
            catch (Exception ex)
            {
                return McpCallResult.FromError("MCP 调用失败: " + ex.Message);
            }

            if (resp["error"] != null)
            {
                return McpCallResult.FromError(FormatJsonRpcError(resp["error"] as JObject, name));
            }

            var result = resp["result"] as JObject;
            if (result == null)
            {
                return McpCallResult.FromError($"工具 '{name}' 返回了空结果。");
            }

            string resultType = result["resultType"]?.Value<string>();
            if (string.Equals(resultType, "input_required", StringComparison.OrdinalIgnoreCase))
            {
                return McpCallResult.FromError($"工具 '{name}' 需要额外输入，暂不支持 (input_required)。");
            }

            string text = FlattenContent(result);
            bool isError = result["isError"]?.Value<bool>() ?? false;
            return new McpCallResult
            {
                Success = !isError,
                Text = text,
                Raw = result,
                Error = isError ? text : null
            };
        }

        private static string FlattenContent(JObject result)
        {
            var sb = new System.Text.StringBuilder();
            var content = result["content"] as JArray;
            if (content != null)
            {
                foreach (var c in content)
                {
                    var obj = c as JObject;
                    if (obj == null) continue;
                    string type = obj["type"]?.Value<string>();
                    if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        string text = obj["text"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(text);
                        }
                    }
                }
            }

            var structured = result["structuredContent"];
            if (structured != null && structured.Type != JTokenType.Null)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(structured.ToString(Formatting.None));
            }

            return sb.ToString();
        }

        private static string FormatJsonRpcError(JObject error, string toolName)
        {
            if (error == null) return $"工具 '{toolName}' 调用失败。";
            int code = error["code"]?.Value<int>() ?? 0;
            string message = error["message"]?.Value<string>() ?? "unknown error";
            string kind;
            switch (code)
            {
                case -32601: kind = "Tool not found"; break;
                case -32603: kind = "Tool execution failed"; break;
                case -32002: kind = "Resource not found"; break;
                case -32022: kind = "Unsupported protocol version"; break;
                default: kind = "error " + code; break;
            }
            string data = error["data"] != null && error["data"].Type != JTokenType.Null
                ? error["data"].ToString(Formatting.None)
                : null;
            return string.IsNullOrWhiteSpace(data)
                ? $"工具 '{toolName}' 失败 ({kind}): {message}"
                : $"工具 '{toolName}' 失败 ({kind}): {message} data={data}";
        }

        public async Task StopAsync()
        {
            if (_transport != null)
            {
                await _transport.StopAsync().ConfigureAwait(false);
            }
            _started = false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _transport?.Dispose();
            _transport = null;
            _started = false;
        }
    }
}
