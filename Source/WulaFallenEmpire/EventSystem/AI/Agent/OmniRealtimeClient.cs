using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// Qwen-Omni-Realtime WebSocket 客户端
    /// 用于实时流式图像输入和文本输出
    /// </summary>
    public class OmniRealtimeClient : IDisposable
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationSource;
        private readonly string _apiKey;
        private readonly string _model;
        private bool _isConnected;
        private bool _isDisposed;
        
        private readonly Queue<string> _pendingResponses = new Queue<string>();
        private readonly StringBuilder _currentResponse = new StringBuilder();
        
        // 事件
        public event Action<string> OnTextDelta;
        public event Action<string> OnTextComplete;
        public event Action<string> OnError;
        public event Action OnConnected;
        public event Action OnDisconnected;
        
        // WebSocket 端点
        private const string WS_ENDPOINT_CN = "wss://dashscope.aliyuncs.com/api-ws/v1/realtime";
        private const string WS_ENDPOINT_INTL = "wss://dashscope-intl.aliyuncs.com/api-ws/v1/realtime";
        
        public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;
        
        public OmniRealtimeClient(string apiKey, string model = "qwen3-omni-flash-realtime")
        {
            _apiKey = apiKey;
            _model = model;
        }
        
        /// <summary>
        /// 建立 WebSocket 连接
        /// </summary>
        public async Task ConnectAsync(bool useInternational = false)
        {
            if (_isConnected) return;
            
            try
            {
                _webSocket = new ClientWebSocket();
                _cancellationSource = new CancellationTokenSource();
                
                // 设置认证头
                _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                
                // 构建连接 URL
                string endpoint = useInternational ? WS_ENDPOINT_INTL : WS_ENDPOINT_CN;
                string url = $"{endpoint}?model={_model}";
                
                WulaLog.Debug($"[OmniRealtime] Connecting to {url}");
                
                await _webSocket.ConnectAsync(new Uri(url), _cancellationSource.Token);
                
                if (_webSocket.State == WebSocketState.Open)
                {
                    _isConnected = true;
                    WulaLog.Debug("[OmniRealtime] Connected successfully");
                    
                    // 启动接收循环
                    _ = ReceiveLoopAsync();
                    
                    // 配置会话（仅文本输出，禁用 VAD）
                    await ConfigureSessionAsync();
                    
                    OnConnected?.Invoke();
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Connection failed: {ex.Message}");
                OnError?.Invoke($"连接失败: {ex.Message}");
                _isConnected = false;
            }
        }
        
        /// <summary>
        /// 配置会话参数
        /// </summary>
        private async Task ConfigureSessionAsync()
        {
            var sessionConfig = new
            {
                event_id = GenerateEventId(),
                type = "session.update",
                session = new
                {
                    // 仅输出文本（不需要音频）
                    modalities = new[] { "text" },
                    // 系统指令
                    instructions = @"你是一个 RimWorld 游戏 AI 代理。你可以看到游戏屏幕截图。
分析屏幕内容，识别重要元素（殖民者、资源、威胁、建筑等）。
根据观察做出决策，输出 XML 格式的工具调用。
可用工具: designate_mine, draft_pawn, visual_click, get_game_state 等。
如果不需要操作，输出 <no_action/>。
保持简洁，直接输出工具调用，不要解释。",
                    // 禁用 VAD（手动模式，因为我们不使用音频）
                    turn_detection = (object)null
                }
            };
            
            await SendEventAsync(sessionConfig);
        }
        
        /// <summary>
        /// 发送图像到服务端
        /// </summary>
        public async Task SendImageAsync(string base64Image)
        {
            if (!IsConnected)
            {
                WulaLog.Debug("[OmniRealtime] Not connected, cannot send image");
                return;
            }
            
            try
            {
                // 发送图像
                var imageEvent = new
                {
                    event_id = GenerateEventId(),
                    type = "input_image_buffer.append",
                    image = base64Image
                };
                
                await SendEventAsync(imageEvent);
                WulaLog.Debug($"[OmniRealtime] Sent image ({base64Image.Length} chars)");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Failed to send image: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送文本消息并请求响应
        /// </summary>
        public async Task SendTextAndRequestResponseAsync(string text)
        {
            if (!IsConnected) return;
            
            try
            {
                // 对于手动模式，需要发送 conversation.item.create 和 response.create
                var itemEvent = new
                {
                    event_id = GenerateEventId(),
                    type = "conversation.item.create",
                    item = new
                    {
                        type = "message",
                        role = "user",
                        content = new[]
                        {
                            new { type = "input_text", text = text }
                        }
                    }
                };
                
                await SendEventAsync(itemEvent);
                
                // 请求响应
                var responseEvent = new
                {
                    event_id = GenerateEventId(),
                    type = "response.create"
                };
                
                await SendEventAsync(responseEvent);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Failed to send text: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送图像并请求分析
        /// </summary>
        public async Task SendImageAndRequestAnalysisAsync(string base64Image, string prompt = "分析当前游戏画面，决定下一步操作")
        {
            if (!IsConnected) return;
            
            try
            {
                // 先发送图像
                await SendImageAsync(base64Image);
                
                // 提交输入并请求响应
                var commitEvent = new
                {
                    event_id = GenerateEventId(),
                    type = "input_audio_buffer.commit" // 这会同时提交图像缓冲区
                };
                await SendEventAsync(commitEvent);
                
                // 发送文本提示
                await SendTextAndRequestResponseAsync(prompt);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Failed to send image for analysis: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 接收消息循环
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();
            
            try
            {
                while (_webSocket?.State == WebSocketState.Open && !_cancellationSource.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _webSocket.ReceiveAsync(segment, _cancellationSource.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        WulaLog.Debug("[OmniRealtime] Server closed connection");
                        break;
                    }
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuffer.Append(chunk);
                        
                        if (result.EndOfMessage)
                        {
                            ProcessServerEvent(messageBuffer.ToString());
                            messageBuffer.Clear();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Receive error: {ex.Message}");
                OnError?.Invoke($"接收错误: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                OnDisconnected?.Invoke();
            }
        }
        
        /// <summary>
        /// 处理服务端事件
        /// </summary>
        private void ProcessServerEvent(string json)
        {
            try
            {
                // 简单解析 JSON 获取事件类型和内容
                // 注意：这里使用简单的字符串解析，生产环境应使用 JSON 库
                
                string eventType = ExtractJsonValue(json, "type");
                
                switch (eventType)
                {
                    case "session.created":
                    case "session.updated":
                        WulaLog.Debug($"[OmniRealtime] Session event: {eventType}");
                        break;
                        
                    case "response.text.delta":
                        string textDelta = ExtractJsonValue(json, "delta");
                        if (!string.IsNullOrEmpty(textDelta))
                        {
                            _currentResponse.Append(textDelta);
                            OnTextDelta?.Invoke(textDelta);
                        }
                        break;
                        
                    case "response.text.done":
                        string completeText = _currentResponse.ToString();
                        _currentResponse.Clear();
                        OnTextComplete?.Invoke(completeText);
                        WulaLog.Debug($"[OmniRealtime] Response complete: {completeText.Substring(0, Math.Min(100, completeText.Length))}...");
                        break;
                        
                    case "response.audio_transcript.delta":
                        // 音频转录的文本增量（如果启用了音频输出）
                        string transcriptDelta = ExtractJsonValue(json, "delta");
                        if (!string.IsNullOrEmpty(transcriptDelta))
                        {
                            _currentResponse.Append(transcriptDelta);
                            OnTextDelta?.Invoke(transcriptDelta);
                        }
                        break;
                        
                    case "response.audio_transcript.done":
                        string transcript = _currentResponse.ToString();
                        _currentResponse.Clear();
                        OnTextComplete?.Invoke(transcript);
                        break;
                        
                    case "error":
                        string errorMsg = ExtractJsonValue(json, "message") ?? json;
                        WulaLog.Debug($"[OmniRealtime] Error: {errorMsg}");
                        OnError?.Invoke(errorMsg);
                        break;
                        
                    case "response.done":
                        // 响应完成
                        break;
                        
                    default:
                        // 其他事件
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"[OmniRealtime] Event: {eventType}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Failed to process event: {ex.Message}");
            }
        }
        
        private async Task SendEventAsync(object eventObj)
        {
            if (_webSocket?.State != WebSocketState.Open) return;
            
            string json = ToSimpleJson(eventObj);
            byte[] data = Encoding.UTF8.GetBytes(json);
            
            await _webSocket.SendAsync(
                new ArraySegment<byte>(data),
                WebSocketMessageType.Text,
                true,
                _cancellationSource.Token
            );
        }
        
        private static string GenerateEventId()
        {
            return $"event_{Guid.NewGuid():N}".Substring(0, 24);
        }
        
        /// <summary>
        /// 简单的对象转 JSON（避免依赖外部库）
        /// </summary>
        private static string ToSimpleJson(object obj)
        {
            // 对于复杂对象，建议使用 Newtonsoft.Json 或 System.Text.Json
            // 这里使用简化实现
            var sb = new StringBuilder();
            SerializeObject(sb, obj);
            return sb.ToString();
        }
        
        private static void SerializeObject(StringBuilder sb, object obj)
        {
            if (obj == null)
            {
                sb.Append("null");
                return;
            }
            
            var type = obj.GetType();
            
            if (obj is string str)
            {
                sb.Append('"');
                sb.Append(str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r"));
                sb.Append('"');
            }
            else if (obj is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (obj is int || obj is long || obj is float || obj is double)
            {
                sb.Append(obj.ToString());
            }
            else if (type.IsArray)
            {
                sb.Append('[');
                var arr = (Array)obj;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializeObject(sb, arr.GetValue(i));
                }
                sb.Append(']');
            }
            else if (type.IsClass)
            {
                sb.Append('{');
                bool first = true;
                foreach (var prop in type.GetProperties())
                {
                    var value = prop.GetValue(obj);
                    if (value == null) continue;
                    
                    if (!first) sb.Append(',');
                    first = false;
                    
                    sb.Append('"');
                    sb.Append(prop.Name);
                    sb.Append("\":");
                    SerializeObject(sb, value);
                }
                // 匿名类型使用字段
                foreach (var field in type.GetFields())
                {
                    var value = field.GetValue(obj);
                    if (value == null) continue;
                    
                    if (!first) sb.Append(',');
                    first = false;
                    
                    sb.Append('"');
                    sb.Append(field.Name);
                    sb.Append("\":");
                    SerializeObject(sb, value);
                }
                sb.Append('}');
            }
        }
        
        private static string ExtractJsonValue(string json, string key)
        {
            // 简单提取 JSON 值
            string pattern = $"\"{key}\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return null;
            
            idx += pattern.Length;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            
            if (idx >= json.Length) return null;
            
            if (json[idx] == '"')
            {
                // 字符串值
                idx++;
                int end = idx;
                while (end < json.Length && json[end] != '"')
                {
                    if (json[end] == '\\') end++; // 跳过转义字符
                    end++;
                }
                return json.Substring(idx, end - idx).Replace("\\n", "\n").Replace("\\\"", "\"");
            }
            else
            {
                // 其他值
                int end = idx;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
                {
                    end++;
                }
                return json.Substring(idx, end - idx).Trim();
            }
        }
        
        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!_isConnected) return;
            
            try
            {
                _cancellationSource?.Cancel();
                
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[OmniRealtime] Disconnect error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                _webSocket?.Dispose();
                _webSocket = null;
            }
        }
        
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            _cancellationSource?.Cancel();
            _cancellationSource?.Dispose();
            _webSocket?.Dispose();
        }
    }
}
