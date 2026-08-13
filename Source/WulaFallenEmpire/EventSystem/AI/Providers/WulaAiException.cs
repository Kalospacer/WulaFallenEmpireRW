using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Structured failure for the AI pipeline plus the shared retry/backoff and SSE idle-watchdog
    /// helpers. Modeled on codex's protocol/error.rs + responses_retry.rs: providers classify failures
    /// into <see cref="AIErrorKind"/> so the agent loop can tell "retry this" from "surface this"
    /// without parsing message strings. The text that reaches the conversation still goes through
    /// <c>AIIntelligenceCore.ErrorPrefix</c> — that contract is display-layer only.
    /// </summary>
    public enum AIErrorKind
    {
        Unknown,
        /// <summary>429 / 408 / 5xx / transport failure / stream idle — eligible for retry with backoff.</summary>
        Retryable,
        /// <summary>4xx (non-429/408) or a definitive API rejection — never retried.</summary>
        Fatal,
        /// <summary>The caller's own cancellation token fired (Stop button, AI disabled, per-request timeout).</summary>
        Cancelled
    }

    public sealed class WulaAiException : Exception
    {
        public AIErrorKind Kind { get; }
        public int? StatusCode { get; }
        public string Provider { get; }
        /// <summary>Parsed from the Retry-After response header when present.</summary>
        public TimeSpan? RetryAfter { get; }

        public WulaAiException(AIErrorKind kind, string message, int? statusCode = null, string provider = null, Exception inner = null, TimeSpan? retryAfter = null)
            : base(message, inner)
        {
            Kind = kind;
            StatusCode = statusCode;
            Provider = provider;
            RetryAfter = retryAfter;
        }

        public static WulaAiException FromHttpStatus(string provider, int statusCode, string body, TimeSpan? retryAfter = null)
        {
            var kind = IsRetryableStatus(statusCode) ? AIErrorKind.Retryable : AIErrorKind.Fatal;
            return new WulaAiException(kind, $"{provider} API error {statusCode}: {TruncateBody(body)}", statusCode, provider, null, retryAfter);
        }

        public static bool IsRetryableStatus(int statusCode)
        {
            return statusCode == 429 || statusCode == 408 || statusCode >= 500;
        }

        public static bool IsRetryable(Exception ex)
        {
            if (ex is WulaAiException wula) return wula.Kind == AIErrorKind.Retryable;
            if (ex is OperationCanceledException) return false;
            // Socket resets / DNS failures / TLS errors all surface as HttpRequestException.
            if (ex is HttpRequestException) return true;
            return false;
        }

        private static string TruncateBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            return body.Length <= 500 ? body : body.Substring(0, 500) + "...";
        }
    }

    /// <summary>
    /// Retry helper shared by all providers. Every attempt gets a fresh linked timeout CTS (after
    /// CancelAfter fires, the token source is spent), and transient failures back off exponentially
    /// while definitive 4xx rejections and caller cancellation throw through immediately.
    /// </summary>
    internal static class AIRequestRetry
    {
        private const int MaxAttempts = 3;
        private const int BaseDelayMs = 500;
        private const int MaxDelayMs = 8000;
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Runs <paramref name="operation"/> up to MaxAttempts times while it fails with a retryable
        /// error. The operation receives the 1-based attempt number and must build its per-attempt
        /// timeout CTS itself.
        /// </summary>
        public static async Task<T> RunAsync<T>(
            string provider,
            AIProviderRequest request,
            CancellationToken cancellationToken,
            Func<int, CancellationToken, Task<T>> operation)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation(attempt, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (WulaAiException.IsRetryable(ex) && attempt < MaxAttempts)
                {
                    TimeSpan delay = ComputeDelay(ex, attempt);
                    AIProviderJson.LogStage(provider, request,
                        $"attempt {attempt}/{MaxAttempts} failed ({ex.GetType().Name}: {Truncate(ex.Message)}); retrying in {delay.TotalSeconds:0.0}s");
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        private static TimeSpan ComputeDelay(Exception ex, int attempt)
        {
            var retryAfter = (ex as WulaAiException)?.RetryAfter;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                return retryAfter.Value > MaxRetryAfter ? MaxRetryAfter : retryAfter.Value;
            }
            int ms = Math.Min(MaxDelayMs, BaseDelayMs * (1 << (attempt - 1)));
            return TimeSpan.FromMilliseconds(ms);
        }

        private static string Truncate(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            return message.Length <= 200 ? message : message.Substring(0, 200) + "...";
        }

        /// <summary>
        /// Reads the SSE body line by line and cancels a linked CTS when no data arrives for
        /// <paramref name="idleTimeout"/>. The idle watchdog complements the whole-request timeout: a
        /// hung-but-alive SSE connection trips the watchdog and unwinds as a retryable stream failure
        /// instead of blocking until the full request budget expires.
        /// </summary>
        public static async Task<int> ReadSseWithIdleWatchdogAsync(
            HttpResponseMessage response,
            Action<string, string> onEvent,
            CancellationToken cancellationToken,
            TimeSpan idleTimeout,
            Action<string> onRawData = null)
        {
            using (var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Action poke = () =>
                {
                    try { idleCts.CancelAfter(idleTimeout); }
                    catch (ObjectDisposedException) { /* stream torn down mid-poke */ }
                };
                try
                {
                    poke();
                    return await AIProviderJson.ReadSseAsync(response, onEvent, idleCts.Token, onRawData, poke);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && idleCts.IsCancellationRequested)
                {
                    throw new WulaAiException(AIErrorKind.Retryable,
                        $"SSE stream idle for {idleTimeout.TotalSeconds:0}s");
                }
            }
        }
    }
}
