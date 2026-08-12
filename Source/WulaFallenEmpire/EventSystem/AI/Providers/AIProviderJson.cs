using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI
{
    internal static class AIProviderJson
    {
        private const int MaxLoggedBodyChars = 50000;
        private static readonly Regex SensitiveQueryRegex = new Regex(@"(?i)([?&](?:key|api_key|access_token)=)([^&]+)", RegexOptions.Compiled);
        private static readonly HashSet<string> SensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization",
            "x-api-key",
            "api-key",
            "cookie",
            "set-cookie"
        };

        public static readonly HttpClient HttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        public static JObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            return JObject.Parse(json);
        }

        public static JObject AsObject(JToken token)
        {
            return token as JObject ?? new JObject();
        }

        public static JObject CloneObject(JObject obj)
        {
            return obj == null ? new JObject() : (JObject)obj.DeepClone();
        }

        public static string Compact(JToken token)
        {
            return token == null ? "{}" : token.ToString(Formatting.None);
        }

        public static StringContent JsonContent(JObject payload)
        {
            return new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        public static string EnsureRequestId(AIProviderRequest request)
        {
            if (request == null) return "wulaai_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                request.RequestId = "wulaai_" + Guid.NewGuid().ToString("N");
            }
            return request.RequestId;
        }

        public static int ClampTimeoutSeconds(AIProviderRequest request)
        {
            int seconds = request?.TimeoutSeconds ?? 120;
            return Math.Max(2, Math.Min(600, seconds));
        }

        public static CancellationTokenSource CreateTimeoutToken(AIProviderRequest request, CancellationToken cancellationToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(ClampTimeoutSeconds(request)));
            return cts;
        }

        public static Stopwatch StartRequest(string provider, AIProviderRequest request, string mode)
        {
            string requestId = EnsureRequestId(request);
            int messageCount = request?.Messages?.Count ?? 0;
            int toolCount = request?.Tools?.Count ?? 0;
            LogStage(provider, request, $"{mode} start model='{request?.Model ?? "(default)"}' stream={request?.Stream == true} messages={messageCount} tools={toolCount} timeout={ClampTimeoutSeconds(request)}s");
            return Stopwatch.StartNew();
        }

        public static void LogStage(string provider, AIProviderRequest request, string message)
        {
            string line = $"[WulaAI][{EnsureRequestId(request)}][{provider}] {message}";
            if (request?.LogRawTraffic == true)
            {
                Log.Message(line);
                return;
            }
            WulaLog.Debug(line);
        }

        public static void LogRawRequest(string provider, AIProviderRequest request, HttpRequestMessage httpRequest, JObject payload)
        {
            if (request?.LogRawTraffic != true || httpRequest == null) return;
            var sb = new StringBuilder();
            sb.Append(httpRequest.Method.Method).Append(' ').Append(SanitizeUrl(httpRequest.RequestUri?.ToString())).AppendLine();
            foreach (var header in httpRequest.Headers)
            {
                sb.Append(header.Key).Append(": ").Append(RedactHeader(header.Key, string.Join(",", header.Value.ToArray()))).AppendLine();
            }
            if (httpRequest.Content != null)
            {
                foreach (var header in httpRequest.Content.Headers)
                {
                    sb.Append(header.Key).Append(": ").Append(string.Join(",", header.Value.ToArray())).AppendLine();
                }
            }
            sb.AppendLine();
            sb.Append(payload == null ? "" : payload.ToString(Formatting.None));
            LogStage(provider, request, "raw request:\n" + TruncateLoggedText(sb.ToString()));
        }

        public static void LogRawResponse(string provider, AIProviderRequest request, int statusCode, string body)
        {
            if (request?.LogRawTraffic != true) return;
            LogStage(provider, request, $"raw response status={statusCode}:\n{TruncateLoggedText(body)}");
        }

        public static void LogUsage(string provider, AIProviderRequest request, AIProviderResponse response)
        {
            if (response?.Usage == null) return;
            LogUsage(provider, request, response.Usage);
        }

        public static void LogUsage(string provider, AIProviderRequest request, JObject usage)
        {
            if (usage == null || usage.Count == 0) return;
            string summary = BuildUsageSummary(usage);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                LogStage(provider, request, summary);
            }
        }

        public static string DescribeCancellation(AIProviderRequest request, CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested
                ? $"cancelled or timed out after {ClampTimeoutSeconds(request)}s"
                : "cancelled";
        }

        private static string BuildUsageSummary(JObject usage)
        {
            long promptTokens = FirstLong(usage, "prompt_tokens", "input_tokens", "promptTokenCount", "totalTokenCount");
            long completionTokens = FirstLong(usage, "completion_tokens", "output_tokens", "candidatesTokenCount");
            long totalTokens = FirstLong(usage, "total_tokens", "totalTokenCount");

            long deepSeekHit = FirstLong(usage, "prompt_cache_hit_tokens");
            long deepSeekMiss = FirstLong(usage, "prompt_cache_miss_tokens");
            if (deepSeekHit >= 0 || deepSeekMiss >= 0)
            {
                long hit = Math.Max(0, deepSeekHit);
                long miss = Math.Max(0, deepSeekMiss);
                long denominator = hit + miss;
                if (promptTokens < 0 && denominator > 0) promptTokens = denominator;
                return FormatUsage("deepseek", promptTokens, completionTokens, totalTokens, hit, miss, denominator);
            }

            long openAiCached = FirstLong(usage, "prompt_tokens_details.cached_tokens", "input_tokens_details.cached_tokens");
            if (openAiCached >= 0)
            {
                long denominator = promptTokens > 0 ? promptTokens : openAiCached;
                long miss = Math.Max(0, denominator - openAiCached);
                return FormatUsage("openai", promptTokens, completionTokens, totalTokens, openAiCached, miss, denominator);
            }

            long anthropicRead = FirstLong(usage, "cache_read_input_tokens");
            long anthropicCreation = FirstLong(usage, "cache_creation_input_tokens");
            if (anthropicRead >= 0 || anthropicCreation >= 0)
            {
                long hit = Math.Max(0, anthropicRead);
                long miss = Math.Max(0, anthropicCreation);
                long denominator = hit + miss;
                return FormatUsage("anthropic", promptTokens, completionTokens, totalTokens, hit, miss, denominator);
            }

            long geminiCached = FirstLong(usage, "cachedContentTokenCount");
            if (geminiCached >= 0)
            {
                long denominator = promptTokens > 0 ? promptTokens : geminiCached;
                long miss = Math.Max(0, denominator - geminiCached);
                return FormatUsage("gemini", promptTokens, completionTokens, totalTokens, geminiCached, miss, denominator);
            }

            if (promptTokens >= 0 || completionTokens >= 0 || totalTokens >= 0)
            {
                return $"usage promptTokens={FormatLong(promptTokens)} completionTokens={FormatLong(completionTokens)} totalTokens={FormatLong(totalTokens)} cacheHitTokens=n/a cacheMissTokens=n/a cacheHitRate=n/a";
            }
            return null;
        }

        private static string FormatUsage(string source, long promptTokens, long completionTokens, long totalTokens, long hitTokens, long missTokens, long denominator)
        {
            string rate = denominator > 0
                ? (hitTokens * 100.0 / denominator).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%"
                : "n/a";
            return $"usage promptTokens={FormatLong(promptTokens)} completionTokens={FormatLong(completionTokens)} totalTokens={FormatLong(totalTokens)} cacheHitTokens={hitTokens} cacheMissTokens={missTokens} cacheHitRate={rate} source={source}";
        }

        private static string FormatLong(long value)
        {
            return value >= 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";
        }

        private static long FirstLong(JObject obj, params string[] paths)
        {
            if (obj == null || paths == null) return -1;
            foreach (var path in paths)
            {
                long value = LongAtPath(obj, path);
                if (value >= 0) return value;
            }
            return -1;
        }

        private static long LongAtPath(JObject obj, string path)
        {
            if (obj == null || string.IsNullOrWhiteSpace(path)) return -1;
            JToken current = obj;
            foreach (var part in path.Split('.'))
            {
                var currentObj = current as JObject;
                if (currentObj == null) return -1;
                current = currentObj[part];
                if (current == null) return -1;
            }
            if (current.Type == JTokenType.Integer) return current.Value<long>();
            if (current.Type == JTokenType.Float) return (long)Math.Round(current.Value<double>());
            if (current.Type == JTokenType.String && long.TryParse(current.Value<string>(), out long parsed)) return parsed;
            return -1;
        }

        private static string RedactHeader(string name, string value)
        {
            if (SensitiveHeaders.Contains(name ?? string.Empty)) return "<redacted>";
            return value ?? string.Empty;
        }

        private static string SanitizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            return SensitiveQueryRegex.Replace(url, "$1<redacted>");
        }

        private static string TruncateLoggedText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= MaxLoggedBodyChars) return text ?? string.Empty;
            return text.Substring(0, MaxLoggedBodyChars) + $"\n...[truncated {text.Length - MaxLoggedBodyChars} chars]";
        }

        public static string NormalizeBaseUrl(string baseUrl, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl.Trim();
            return value.TrimEnd('/');
        }

        public static JObject ParseMaybeObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new JObject();
            try
            {
                var token = JToken.Parse(value);
                return token as JObject ?? new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        public static async Task<int> ReadSseAsync(HttpResponseMessage response, Action<string, string> onEvent, CancellationToken cancellationToken, Action<string> onRawData = null)
        {
            string eventName = null;
            int dataLineCount = 0;
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                // Created once for the whole stream: an infinite Task.Delay holds a registration on
                // the token until the token is disposed, so building one per line would accumulate
                // one live registration for every SSE line read.
                var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var readTask = reader.ReadLineAsync();
                    var completed = await Task.WhenAny(readTask, cancelTask);
                    if (completed == cancelTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    string line = await readTask;
                    if (line == null) break;
                    if (line.Length == 0)
                    {
                        eventName = null;
                        continue;
                    }
                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventName = line.Substring(6).Trim();
                        continue;
                    }
                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        string data = line.Substring(5).Trim();
                        dataLineCount++;
                        onRawData?.Invoke(data);
                        onEvent?.Invoke(eventName, data);
                    }
                }
            }
            return dataLineCount;
        }

        public static string FirstText(List<AIContentPart> parts)
        {
            if (parts == null) return null;
            foreach (var part in parts)
            {
                if (part != null && string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Text;
                }
            }
            return null;
        }
    }
}
