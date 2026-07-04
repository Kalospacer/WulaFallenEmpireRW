using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    internal static class AIProviderJson
    {
        public static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

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

        public static async Task ReadSseAsync(HttpResponseMessage response, Action<string, string> onEvent, CancellationToken cancellationToken)
        {
            string eventName = null;
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string line = await reader.ReadLineAsync();
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
                        onEvent?.Invoke(eventName, line.Substring(5).Trim());
                    }
                }
            }
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
