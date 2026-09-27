using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Caret.Launcher
{
    // Starts app\Caret.exe with the same command line (a file dropped on the launcher, or passed from
    // a terminal) and exits.
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app", "Caret.exe");
            if (!File.Exists(app))
            {
                ShowError($"Caret couldn't find its program files:\n{app}\n\nUnzip the whole Caret-Portable folder, not just Caret.exe.");
                return 1;
            }
            try
            {
                Process.Start(new ProcessStartInfo(app, string.Join(" ", args.Select(QuoteArgument))) { UseShellExecute = false });
                return 0;
            }
            catch (Exception ex)
            {
                ShowError($"Caret couldn't start:\n{ex.Message}");
                return 1;
            }
        }

        // Windows command-line quoting (the rules CommandLineToArgvW parses): quote when needed, and
        // double the backslashes that precede a quote.
        internal static string QuoteArgument(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '"' }) < 0) return arg;
            var result = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                result.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                result.Append(c);
            }
            result.Append('\\', backslashes * 2);
            return result.Append('"').ToString();
        }

        private static void ShowError(string message) => MessageBoxW(IntPtr.Zero, message, "Caret", 0x10 /* MB_ICONERROR */);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
