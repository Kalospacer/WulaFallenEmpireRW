using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WulaFallenEmpire.EventSystem.AI.Bridge;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 进程内调用 RimBridgeServer 的一个游戏工具（如 rimworld/click_cell、rimworld/take_screenshot）。
    /// 对截图类工具，会把截图文件复制进图库并作为多模态图片结果返回，让模型能真正看到画面。
    /// </summary>
    public class Tool_BridgeCall : AITool
    {
        public override string Name => "bridge_call";
        public override string Description =>
            "进程内调用 RimBridgeServer 的一个游戏操作工具并返回结果。参数：tool（工具 id 或别名，"
            + "来自 bridge_list_tools）、arguments（JSON 对象，按 bridge_list_tools 返回的参数填）、"
            + "timeout（可选秒数）。只读工具优先；改动类（点击/存档/推进时间）确认后再做。"
            + "截图类工具（take_screenshot / screenshot_cell_rect）会把画面作为图片返回，你能看到。";

        private static readonly HashSet<string> ScreenshotToolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rimworld/take_screenshot",
            "rimworld/screenshot_cell_rect",
            "rimworld/frame_cell_rect",
            "take_screenshot",
            "screenshot_cell_rect",
            "frame_cell_rect"
        };

        private string _screenshotFileName;

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["tool"] = SchemaString("工具 id 或别名（bridge_list_tools 返回的）。", nullable: false),
                ["arguments"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "传给工具的 JSON 参数对象。",
                    ["additionalProperties"] = true
                },
                ["timeout"] = SchemaInteger("超时秒数（可选）。", nullable: true)
            };
            return SchemaObject(properties, RequiredList("tool"));
        }

        public override async Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _screenshotFileName = null;
            try
            {
                var parsed = ParseJsonArgs(args);
                if (!TryGetString(parsed, "tool", out var tool) || string.IsNullOrWhiteSpace(tool))
                    return "Error: 缺少 tool。";

                Dictionary<string, object> callArgs = null;
                if (TryGetObject(parsed, "arguments", out var argsDict) && argsDict != null)
                {
                    callArgs = argsDict;
                }

                string textResult;
                if (TryGetInt(parsed, "timeout", out var timeoutSec) && timeoutSec > 0)
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                        textResult = await RimBridgeFacade.CallAsync(tool, callArgs, cts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    textResult = await RimBridgeFacade.CallAsync(tool, callArgs, cancellationToken).ConfigureAwait(false);
                }

                // 截图类工具：把返回的图片文件复制进图库，稍后经 GetResultParts 作为图片喂给模型。
                if (ScreenshotToolIds.Contains(tool) && !textResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                    && !textResult.StartsWith("失败", StringComparison.Ordinal))
                {
                    TryImportScreenshot(textResult);
                }
                return textResult;
            }
            catch (OperationCanceledException)
            {
                return "Error: bridge_call 被取消或超时。";
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        /// <summary>
        /// 从截图工具的文本结果里找出图片文件路径，复制进图库并记录图库文件名。
        /// </summary>
        private void TryImportScreenshot(string textResult)
        {
            try
            {
                string path = ResolveExistingImagePath(FindImagePath(textResult));
                if (path == null) return;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0) return;
                _screenshotFileName = AIImageStore.SaveImage(bytes);
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[Tool_BridgeCall] TryImportScreenshot failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Turns the bridge's reported path into an existing absolute path, or null when nothing matches.
        /// A relative path would otherwise be resolved against the process working directory, which is not
        /// where RimWorld writes screenshots, so the candidate roots are tried explicitly.
        /// </summary>
        private static string ResolveExistingImagePath(string reported)
        {
            if (string.IsNullOrWhiteSpace(reported)) return null;
            string candidate = reported.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(candidate))
                {
                    return File.Exists(candidate) ? candidate : null;
                }
                foreach (string root in new[]
                {
                    Verse.GenFilePaths.SaveDataFolderPath,
                    Directory.GetCurrentDirectory()
                })
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string combined = Path.Combine(root, candidate);
                    if (File.Exists(combined)) return combined;
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[Tool_BridgeCall] ResolveExistingImagePath failed for '" + candidate + "': " + ex.Message);
            }
            return null;
        }

        private static string FindImagePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            // 结果可能是 JSON（{"path": "..."}）或纯文本路径。先按 JSON 取 path/file/filename 字段，
            // 再退回子串匹配 —— 只认 Windows 盘符 + 反斜杠会漏掉正斜杠、相对路径和 UNC 路径，
            // 那种情况下工具仍会声称「你能看到画面」，而实际没有导入任何图片。
            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(text);
                foreach (var key in new[] { "path", "file", "filename", "filePath", "file_path" })
                {
                    var found = token.SelectToken("$.." + key);
                    if (found == null || found.Type != Newtonsoft.Json.Linq.JTokenType.String) continue;
                    string value = (string)found;
                    if (!string.IsNullOrWhiteSpace(value) && HasImageExtension(value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch
            {
                // 不是 JSON，走下面的子串匹配。
            }

            // 盘符路径 / UNC 路径 / 带分隔符的相对路径，正反斜杠都接受。
            // 相对路径分支要求分隔符前有一段路径名，否则 "Screenshots/shot.png" 会只匹配到 "/shot.png"。
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:[A-Za-z]:[\\/]|\\\\[^\\/\r\n]+[\\/]|\.{1,2}[\\/]|[\w.\-]+[\\/])[^""'\r\n<>|]*?\.(?:png|jpg|jpeg)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) return match.Value.Trim();

            // 最后兜底：裸文件名（无目录部分）。
            var bare = System.Text.RegularExpressions.Regex.Match(
                text,
                @"[^\s""'\\/<>|]+\.(?:png|jpg|jpeg)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return bare.Success ? bare.Value.Trim() : null;
        }

        private static bool HasImageExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string trimmed = path.TrimEnd();
            return trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        public override Task<List<AIContentPart>> GetResultPartsAsync(string argsJson, string textResult, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_screenshotFileName))
            {
                return Task.FromResult<List<AIContentPart>>(null);
            }
            byte[] bytes = AIImageStore.LoadImageBytes(_screenshotFileName);
            if (bytes == null || bytes.Length == 0)
            {
                return Task.FromResult<List<AIContentPart>>(null);
            }
            var parts = new List<AIContentPart>
            {
                AIContentPart.ImagePart("image/jpeg", Convert.ToBase64String(bytes))
            };
            return Task.FromResult(parts);
        }
    }
}
