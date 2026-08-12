using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI.Mcp
{
    /// <summary>
    /// 单个 MCP server 的配置。形状对齐 Codex 的 <c>McpServerConfig</c>（见
    /// codex-rs/config/src/mcp_types.rs），首版裁剪掉 auth/oauth/environment_id。
    /// </summary>
    public sealed class McpServerConfig
    {
        public string Name;
        public string Transport = "stdio";                       // "stdio" | "http"
        public string Command;                                   // stdio: 可执行文件
        public List<string> Args = new List<string>();           // stdio: 参数
        public Dictionary<string, string> Env = new Dictionary<string, string>(); // stdio: 环境变量
        public string Cwd;                                       // stdio: 工作目录（可空）
        public string Url;                                       // http: MCP 端点
        public bool Enabled = true;
        public int StartupTimeoutSec = 30;
        public int ToolTimeoutSec = 120;
        public List<string> EnabledTools;                        // null = 无 allowlist
        public List<string> DisabledTools = new List<string>();  // denylist

        public bool IsStdio => string.Equals(Transport, "stdio", StringComparison.OrdinalIgnoreCase);
        public bool IsHttp => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// skill 依赖匹配用的 canonical key（对齐 Codex 的
        /// <c>mcp__{transport}__{identifier}</c>）。
        /// </summary>
        public string CanonicalKey
        {
            get
            {
                string transport = IsHttp ? "streamable_http" : "stdio";
                string identifier = IsHttp ? (Url ?? string.Empty).Trim() : (Command ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(identifier)) identifier = (Name ?? string.Empty).Trim();
                return $"mcp__{transport}__{identifier}";
            }
        }

        public bool IsToolAllowed(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return true;
            if (DisabledTools != null)
            {
                foreach (var d in DisabledTools)
                {
                    if (string.Equals(d, toolName, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            if (EnabledTools != null && EnabledTools.Count > 0)
            {
                foreach (var e in EnabledTools)
                {
                    if (string.Equals(e, toolName, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 解析 <c>{ "servers": [ ... ] }</c> 形状的 JSON。坏条目不丢整体，逐条返回错误。
        /// </summary>
        public static List<McpServerConfig> ParseMany(string json, out List<string> errors)
        {
            errors = new List<string>();
            var result = new List<McpServerConfig>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                errors.Add("MCP 配置 JSON 解析失败: " + ex.Message);
                return result;
            }

            var servers = root["servers"] as JArray;
            if (servers == null)
            {
                errors.Add("MCP 配置缺少 'servers' 数组。");
                return result;
            }

            foreach (var token in servers)
            {
                var obj = token as JObject;
                if (obj == null) continue;
                try
                {
                    var cfg = Parse(obj);
                    if (cfg == null) continue;
                    result.Add(cfg);
                }
                catch (Exception ex)
                {
                    errors.Add("解析 MCP server 条目失败: " + ex.Message);
                }
            }
            return result;
        }

        public static McpServerConfig Parse(JObject o)
        {
            var name = (o["name"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return null;

            var cfg = new McpServerConfig
            {
                Name = name,
                Transport = o["transport"]?.Value<string>() ?? "stdio",
                Command = o["command"]?.Value<string>(),
                Url = o["url"]?.Value<string>(),
                Cwd = o["cwd"]?.Value<string>(),
                Enabled = o["enabled"]?.Value<bool>() ?? true,
                StartupTimeoutSec = o["startup_timeout_sec"]?.Value<int>() ?? 30,
                ToolTimeoutSec = o["tool_timeout_sec"]?.Value<int>() ?? 120
            };

            var args = o["args"] as JArray;
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (a.Type == JTokenType.String) cfg.Args.Add(a.Value<string>());
                }
            }

            var env = o["env"] as JObject;
            if (env != null)
            {
                foreach (var prop in env.Properties())
                {
                    cfg.Env[prop.Name] = prop.Value?.Value<string>() ?? string.Empty;
                }
            }

            var enabledTools = o["enabled_tools"] as JArray;
            if (enabledTools != null && enabledTools.Count > 0)
            {
                cfg.EnabledTools = new List<string>();
                foreach (var e in enabledTools)
                {
                    if (e.Type == JTokenType.String) cfg.EnabledTools.Add(e.Value<string>());
                }
            }

            var disabledTools = o["disabled_tools"] as JArray;
            if (disabledTools != null)
            {
                foreach (var d in disabledTools)
                {
                    if (d.Type == JTokenType.String) cfg.DisabledTools.Add(d.Value<string>());
                }
            }

            return cfg;
        }

        /// <summary>默认配置：本地 GABS（stdio）作为第一个示例 server。</summary>
        public static string DefaultJson()
        {
            return
@"{
  ""servers"": [
    {
      ""name"": ""gabs"",
      ""transport"": ""stdio"",
      ""command"": ""gabs.exe"",
      ""args"": [""server""],
      ""enabled"": true,
      ""startup_timeout_sec"": 30,
      ""tool_timeout_sec"": 120
    }
  ]
}";
        }
    }
}
