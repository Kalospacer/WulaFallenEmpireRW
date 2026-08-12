using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>
    /// Streamable HTTP 传输：每消息一次 POST 到 MCP 端点，响应可单 JSON 或 SSE。
    /// 复用全局 <see cref="AIProviderJson.HttpClient"/>（无限超时）。
    /// </summary>
    public sealed class HttpMcpTransport : IMcpTransport
    {
        private readonly McpServerConfig _config;
        private bool _ready;
        private int _disposed;

        public HttpMcpTransport(McpServerConfig config)
        {
            _config = config;
        }

        public bool IsReady => _ready;

        public event Action<string> OnLineReceived;
        public event Action<string> OnDisconnected;

        public Task StartAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_config.Url))
            {
                throw new InvalidOperationException($"MCP server '{_config.Name}' 缺少 url。");
            }
            _ready = true;
            return Task.FromResult(true);
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            if (!_ready) throw new InvalidOperationException($"MCP server '{_config.Name}' 未启动。");

            string url = _config.Url.TrimEnd('/');
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(line, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

            try
            {
                using (var response = await AIProviderJson.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        RaiseDisconnected($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                        return;
                    }

                    string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (contentType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        await AIProviderJson.ReadSseAsync(response, (name, data) =>
                        {
                            if (!string.IsNullOrWhiteSpace(data)) RaiseLine(data);
                        }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(body)) RaiseLine(body);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RaiseDisconnected($"HTTP 请求失败: {ex.Message}");
            }
        }

        public Task StopAsync()
        {
            _ready = false;
            return Task.FromResult(true);
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
            _ready = false;
        }
    }
}
