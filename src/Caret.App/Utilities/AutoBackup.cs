using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.WinUI.Utilities
{
    // Ported from Typedown.Core\Services\AutoBackup.cs (and its SimpleHash2/ToBase36String helpers
    // from Typedown.Core\Utilities\Common.cs). Crash/quit recovery: while a document is dirty —
    // including a brand-new untitled document that was never saved anywhere — FileViewModel's timer
    // periodically writes its content here so it can be offered back on next launch if the app
    // disappears before a real save happens. Kept as a static utility (like Win32FolderPicker) since
    // it's stateless aside from the backup folder path, matching how the original's DI-registered
    // AutoBackup service was used — a single shared instance with no per-window state.
    public static class AutoBackup
    {
        private static string BackupFolder => Path.Combine(Config.GetLocalFolderPath(), "Backup");

        // sourcePath is the document's real path, or null/empty for an untitled document — hashed
        // either way so it still gets a stable backup slot instead of being unrepresentable.
        public static string GetBackupFilePath(string sourcePath)
        {
            Directory.CreateDirectory(BackupFolder);
            sourcePath ??= "";
            var hash = SimpleHash2(sourcePath);
            var fileName = string.IsNullOrEmpty(sourcePath) ? "untitled" : Path.GetFileName(sourcePath);
            return Path.Combine(BackupFolder, $"{hash}_{fileName}");
        }

        public static async Task<bool> Backup(string sourcePath, string markdown)
        {
            try
            {
                await File.WriteAllTextAsync(GetBackupFilePath(sourcePath), markdown);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> GetBackup(string sourcePath)
        {
            try
            {
                return await File.ReadAllTextAsync(GetBackupFilePath(sourcePath));
            }
            catch
            {
                return null;
            }
        }

        public static void DeleteBackup(string sourcePath)
        {
            try
            {
                File.Delete(GetBackupFilePath(sourcePath));
            }
            catch
            {
                // Matches the original: best-effort cleanup, nothing to react to if it fails.
            }
        }

        // Verbatim port of Common.SimpleHash2/ToBase36String — same algorithm, so backup filenames
        // would be stable if this ever needs to interoperate with the original app, though nothing
        // relies on that today.
        private static string SimpleHash2(string str)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(str));
            var result = ToBase36String(hash);
            return result.Substring(0, Math.Min(6, result.Length));
        }

        private static string ToBase36String(byte[] toConvert)
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            var dividend = new BigInteger(toConvert);
            var builder = new StringBuilder();
            while (dividend != 0)
            {
                dividend = BigInteger.DivRem(dividend, 36, out var remainder);
                builder.Insert(0, alphabet[Math.Abs((int)remainder)]);
            }
            return builder.Length > 0 ? builder.ToString() : "0";
        }
    }
}
