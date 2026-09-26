using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Typedown.WinUI.Services.MarkItDown
{
    // Portable build: every conversion goes through Microsoft MarkItDown, run by a Python the user
    // installed themselves. Locked-down PCs often let people install Python and pip packages for
    // their own account but not change PATH, so nothing here relies on PATH: it looks in the places
    // Python installers actually put python.exe, then asks each candidate whether it can import
    // markitdown. PATH is only the last resort.
    public sealed record PythonInfo(string Executable, string PythonVersion, string MarkItDownVersion)
    {
        public bool HasMarkItDown => MarkItDownVersion != null;
    }

    public static class PythonLocator
    {
        // Printed by every probe: the interpreter's real path (a py.exe or a WindowsApps alias
        // resolves to it), its version, and markitdown's version or an empty line.
        private const string ProbeScript =
            "import sys\n" +
            "print(sys.executable)\n" +
            "print('%d.%d.%d' % sys.version_info[:3])\n" +
            "try:\n" +
            "    import importlib.metadata as m\n" +
            "    import markitdown\n" +
            "    print(m.version('markitdown'))\n" +
            "except Exception:\n" +
            "    print('')\n";

        // A folder called "python" next to Caret.exe (for example an extracted "embeddable" Python
        // with MarkItDown installed into it), so a team can hand out one folder with everything in it.
        public static string BundledPythonPath => Path.Combine(AppContext.BaseDirectory, "python", "python.exe");

        // The chosen python.exe (the user's pick wins) that can import markitdown; otherwise the first
        // working Python without it, so the UI can say "install MarkItDown" instead of "install Python";
        // null when there's no Python at all.
        public static async Task<PythonInfo> FindAsync(string preferredPath)
        {
            PythonInfo withoutMarkItDown = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (exe, prefixArgs) in Candidates(preferredPath))
            {
                var info = await ProbeAsync(exe, prefixArgs);
                if (info == null || !seen.Add(info.Executable)) continue;
                if (info.HasMarkItDown) return info;
                withoutMarkItDown ??= info;
            }
            return withoutMarkItDown;
        }

        public static async Task<PythonInfo> ProbeAsync(string exe, IEnumerable<string> prefixArgs = null)
        {
            var args = (prefixArgs ?? Array.Empty<string>()).Concat(new[] { "-c", ProbeScript });
            var (exitCode, stdout, _, _) = await ProcessRunner.RunAsync(exe, args, 20_000);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return null;
            var lines = stdout.Replace("\r", "").Split('\n');
            if (lines.Length < 2 || !File.Exists(lines[0].Trim())) return null;
            var markItDown = lines.Length > 2 && lines[2].Trim().Length > 0 ? lines[2].Trim() : null;
            return new PythonInfo(lines[0].Trim(), lines[1].Trim(), markItDown);
        }

        private static IEnumerable<(string exe, string[] prefixArgs)> Candidates(string preferredPath)
        {
            var none = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
                yield return (preferredPath, none);
            if (File.Exists(BundledPythonPath))
                yield return (BundledPythonPath, none);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // The Python launcher picks the newest Python 3 it knows about, PATH or not.
            foreach (var launcher in new[]
            {
                Path.Combine(localAppData, "Programs", "Python", "Launcher", "py.exe"),
                Path.Combine(windows, "py.exe"),
            })
            {
                if (File.Exists(launcher)) yield return (launcher, new[] { "-3" });
            }

            // python.org installer ("just for me" and "all users"), newest version first.
            foreach (var root in new[]
            {
                Path.Combine(localAppData, "Programs", "Python"),
                programFiles,
                programFilesX86,
                Path.GetPathRoot(windows) ?? @"C:\",
            })
            {
                foreach (var exe in VersionedPythons(root)) yield return (exe, none);
            }

            // Anaconda / Miniconda.
            foreach (var dir in new[] { userProfile, localAppData, programData })
            {
                foreach (var name in new[] { "anaconda3", "miniconda3", "Anaconda3", "Miniconda3" })
                {
                    var exe = Path.Combine(dir, name, "python.exe");
                    if (File.Exists(exe)) yield return (exe, none);
                }
            }

            // Microsoft Store Python. Its WindowsApps aliases are reparse points, and the probe
            // resolves them to the real interpreter; the bare "python" alias with no Store Python
            // behind it just fails the probe.
            var windowsApps = Path.Combine(localAppData, "Microsoft", "WindowsApps");
            foreach (var name in new[] { "python3.exe", "python.exe" })
            {
                var exe = Path.Combine(windowsApps, name);
                if (File.Exists(exe)) yield return (exe, none);
            }

            // Last resort: whatever PATH happens to have.
            yield return ("py", new[] { "-3" });
            yield return ("python", none);
            yield return ("python3", none);
        }

        // <root>\Python3xx\python.exe, highest version first (Python313 before Python39).
        private static IEnumerable<string> VersionedPythons(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return Enumerable.Empty<string>();
            try
            {
                return Directory.EnumerateDirectories(root, "Python3*")
                    .Select(dir => (dir, version: ParseFolderVersion(Path.GetFileName(dir))))
                    .OrderByDescending(d => d.version)
                    .Select(d => Path.Combine(d.dir, "python.exe"))
                    .Where(File.Exists)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        // "Python312" → 312, "Python310-32" → 310, "Python313t" → 313.
        private static int ParseFolderVersion(string name)
        {
            var digits = new string(name.Skip("Python".Length).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var value) ? value : 0;
        }
    }
}
