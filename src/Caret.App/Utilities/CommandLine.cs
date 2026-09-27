using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Typedown.WinUI.Utilities
{
    // Ported verbatim from Typedown.Core\Utilities\CommandLine.cs — no UWP dependencies.
    public static class CommandLine
    {
        public static string GetOpenFilePath(string[] commandLineArgs)
        {
            return commandLineArgs?.Where(FileTypeHelper.IsMarkdownFile).FirstOrDefault();
        }

        // Splits a raw command-line string (as handed to us by
        // Microsoft.Windows.AppLifecycle.ILaunchActivatedEventArgs.Arguments on a redirected
        // activation — see Program.cs) into argv the same way the OS itself would, including quoted
        // paths with spaces. .NET has no public API for parsing an arbitrary command-line string
        // (Environment.GetCommandLineArgs() only parses this process's own), so this goes straight to
        // the same shell32 function the OS uses to build argv for a normal process launch.
        public static string[] Split(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return Array.Empty<string>();
            var argv = CommandLineToArgvW(commandLine, out var argc);
            if (argv == IntPtr.Zero) return Array.Empty<string>();
            try
            {
                var result = new string[argc];
                for (var i = 0; i < argc; i++)
                    result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return result;
            }
            finally
            {
                // CommandLineToArgvW's return value is LocalAlloc'd, not HGlobalAlloc'd — LocalFree is
                // the correct release, not Marshal.FreeHGlobal (they aren't interchangeable, even
                // though both happen to be thin wrappers over the same process heap in practice).
                LocalFree(argv);
            }
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argc);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
