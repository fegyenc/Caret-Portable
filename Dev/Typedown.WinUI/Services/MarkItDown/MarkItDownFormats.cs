using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Typedown.WinUI.Services.MarkItDown
{
    public static class MarkItDownFormats
    {
        // What MarkItDown converts offline. Left out on purpose: audio (its transcription sends the
        // recording to an online speech service) and images (without an AI model configured it only
        // returns their metadata).
        public static IReadOnlyList<string> Extensions { get; } = new[]
        {
            ".docx", ".pptx", ".xlsx", ".xls", ".pdf", ".csv",
            ".msg", ".html", ".htm", ".epub", ".ipynb", ".json", ".xml", ".rss", ".txt", ".zip",
        };

        // Older binary Office formats MarkItDown can't read (.xls it can).
        public static IReadOnlyList<string> LegacyOfficeExtensions { get; } = new[] { ".doc", ".ppt", ".dot", ".pot" };

        public static bool IsSupported(string path) =>
            Extensions.Contains(Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase);

        public static bool IsLegacyOffice(string path) =>
            LegacyOfficeExtensions.Contains(Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase);

        // A rough, tokenizer-independent estimate (about four characters per token). Shown with "≈";
        // it's for comparing, not billing.
        public static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);
    }
}
