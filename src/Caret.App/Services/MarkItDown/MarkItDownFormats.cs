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
            ".msg", ".eml", ".html", ".htm", ".epub", ".ipynb", ".json", ".xml", ".rss", ".txt", ".zip",
        };

        // Older binary Office formats MarkItDown can't read (.xls it can).
        public static IReadOnlyList<string> LegacyOfficeExtensions { get; } = new[] { ".doc", ".ppt", ".dot", ".pot" };

        public static bool IsSupported(string path) =>
            Extensions.Contains(Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase);

        public static bool IsLegacyOffice(string path) =>
            LegacyOfficeExtensions.Contains(Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase);

        // Formats MarkItDown reads as text, and so has to know the encoding of.
        private static readonly string[] TextExtensions = { ".csv", ".txt", ".json", ".xml", ".rss", ".html", ".htm" };

        // Bigger text files are left to MarkItDown's own guess rather than read twice.
        private const long MaxCharsetProbeBytes = 64L * 1024 * 1024;

        // The Python codec to decode a text file with, decided the way Caret opens Markdown
        // (Utilities/TextFileEncoding.cs): a byte-order mark decides; otherwise strict UTF-8; otherwise
        // Windows' ANSI code page, which is what Excel's "CSV" and most older tools write. Null for
        // binary formats (Word, PDF…), which carry their own encoding.
        public static string CharsetFor(string path)
        {
            if (!TextExtensions.Contains(Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase)) return null;
            try
            {
                if (new FileInfo(path).Length > MaxCharsetProbeBytes) return null;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return "utf-8-sig";
                if (bytes.Length >= 4 && ((bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
                    || (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF))) return "utf-32";
                if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return "utf-16";
                var legacy = Utilities.TextFileEncoding.Decode(bytes).LegacyEncoding;
                return legacy == null ? "utf-8" : $"cp{legacy.CodePage}";
            }
            catch
            {
                return null; // unreadable here: let MarkItDown report it
            }
        }

        // A rough, tokenizer-independent estimate (about four characters per token). Shown with "≈";
        // it's for comparing, not billing.
        public static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);
    }
}
