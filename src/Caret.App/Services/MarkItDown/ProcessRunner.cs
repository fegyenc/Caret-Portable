using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.WinUI.Services.MarkItDown
{
    public static class ProcessRunner
    {
        // Runs a process to completion with no console window. exitCode is null when the executable
        // couldn't be started at all (missing, or blocked by policy), distinct from a real exit code.
        public static async Task<(int? exitCode, string stdout, string stderr, bool timedOut)> RunAsync(
            string fileName, IEnumerable<string> args, int timeoutMs)
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, args) };
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (null, null, null, false);
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = await Task.Run(() => process.WaitForExit(timeoutMs));
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (null, null, null, true);
            }
            return (process.ExitCode, await stdoutTask, await stderrTask, false);
        }

        public static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // UTF-8 on both ends: Python would otherwise write the console code page to a
                // redirected pipe, and .NET would decode it with the ANSI code page, silently mangling
                // accented, Cyrillic or CJK text.
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONUTF8"] = "1";
            // Keep Python from writing __pycache__ next to a bundled or read-only install.
            startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);
            return startInfo;
        }
    }
}
