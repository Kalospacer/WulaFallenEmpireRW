using System.Text;
using System.Text.RegularExpressions;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    /// <summary>
    /// Converts the model's Markdown output into Unity IMGUI rich-text tags so the AI dialog and overlay
    /// render bold/headers/lists/code/quotes instead of raw markdown characters. Deliberately lightweight
    /// (no external dependency) and block-aware: fenced code blocks are not inline-transformed, and any
    /// literal &lt; &gt; &amp; in the source is escaped first so it can't corrupt the rich-text markup.
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly Regex HeaderRegex = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex OrderedItemRegex = new Regex(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex UnorderedItemRegex = new Regex(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);

        /// <summary>Converts a Markdown string to Unity rich text. Safe to call on plain text too.</summary>
        public static string ToRichText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;
            if (!LooksLikeMarkdown(markdown)) return Escape(markdown);

            var sb = new StringBuilder(markdown.Length + 64);
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inCodeBlock = false;
            foreach (var rawLine in lines)
            {
                string line = rawLine;
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue; // drop the fence markers
                }

                if (inCodeBlock)
                {
                    sb.Append("<color=#e6c07b>").Append(Escape(line)).Append("</color>").Append('\n');
                    continue;
                }

                string converted = ConvertLine(line);
                sb.Append(converted).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        private static string ConvertLine(string line)
        {
            string escaped = Escape(line);

            var header = HeaderRegex.Match(line);
            if (header.Success)
            {
                int level = header.Groups[1].Value.Length;
                int sizeBoost = level == 1 ? 6 : level == 2 ? 4 : 2;
                return $"<size=+{sizeBoost}><b>{ApplyInline(header.Groups[2].Value)}</b></size>";
            }

            string trimmed = escaped.TrimStart();
            string indent = escaped.Substring(0, escaped.Length - trimmed.Length);

            if (trimmed.StartsWith("&gt;"))
            {
                return indent + "<color=#9a9a9a>▍ " + ApplyInline(trimmed.Substring(4).TrimStart()) + "</color>";
            }

            var ordered = OrderedItemRegex.Match(line);
            if (ordered.Success)
            {
                return indent + ordered.Groups[2].Value + ". " + ApplyInline(ordered.Groups[3].Value);
            }

            var unordered = UnorderedItemRegex.Match(line);
            if (unordered.Success)
            {
                return indent + "• " + ApplyInline(unordered.Groups[2].Value);
            }

            return indent + ApplyInline(trimmed);
        }

        /// <summary>Applies inline emphasis/code transforms to an already XML-escaped fragment.</summary>
        private static string ApplyInline(string escapedFragment)
        {
            string s = escapedFragment;
            // inline code first so emphasis markers inside code are left alone
            s = Regex.Replace(s, "`([^`]+)`", "<color=#e6c07b>$1</color>");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            s = Regex.Replace(s, @"__([^_]+)__", "<b>$1</b>");
            s = Regex.Replace(s, @"~~([^~]+)~~", "<color=#9a9a9a>$1</color>");
            // single-asterisk / underscore italics; avoid matching the bold we just made
            s = Regex.Replace(s, @"(?<![\w*])\*([^*\n]+)\*(?![\w*])", "<i>$1</i>");
            s = Regex.Replace(s, @"(?<![\w_])_([^_\n]+)_(?![\w_])", "<i>$1</i>");
            return s;
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>Cheap heuristic: does this contain any markdown construct worth transforming?</summary>
        private static bool LooksLikeMarkdown(string text)
        {
            return text.Contains("**") || text.Contains("__") || text.Contains("```") ||
                   text.Contains("`") || text.Contains("~~") || text.Contains("# ") ||
                   text.Contains("\n- ") || text.Contains("\n* ") || text.Contains("> ");
        }
    }
}
