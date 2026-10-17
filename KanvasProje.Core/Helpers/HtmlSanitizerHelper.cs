using System;
using System.Text.RegularExpressions;

namespace KanvasProje.Core.Helpers
{
    /// <summary>
    /// Kullanıcıya gösterilen zengin metin (Aciklama, Kurumsal Sayfa İçeriği vb.) alanlarında
    /// Stored-XSS vektörlerini (<script>, inline event handler on*, javascript: vb.) temizler.
    /// </summary>
    public static class HtmlSanitizerHelper
    {
        private static readonly Regex ScriptOrDangerousTagsRegex = new(
            @"<\s*(script|iframe|object|embed|form|meta|link|base|applet)\b[^>]*>([\s\S]*?<\s*/\s*\1\s*>)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DangerousSelfClosingTagsRegex = new(
            @"<\s*/?\s*(script|iframe|object|embed|form|meta|link|base|applet)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InlineEventHandlersRegex = new(
            @"\s+on[a-z0-9_]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DangerousProtocolsRegex = new(
            @"(?<attr>\b(?:href|src|xlink:href|action|formaction)\s*=\s*(?:""|')?)\s*(?:javascript|vbscript|data\s*:\s*text/html)\s*:[^""'\s>]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Sanitize(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var cleaned = ScriptOrDangerousTagsRegex.Replace(html, string.Empty);
            cleaned = DangerousSelfClosingTagsRegex.Replace(cleaned, string.Empty);
            cleaned = InlineEventHandlersRegex.Replace(cleaned, string.Empty);
            cleaned = DangerousProtocolsRegex.Replace(cleaned, "${attr}#");
            return cleaned;
        }

        public static string SanitizeUrl(string? url, string fallback = "/Urun")
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return fallback;
            }

            var trimmed = url.Trim();
            if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            return trimmed;
        }
    }
}
