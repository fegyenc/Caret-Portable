using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Services.MarkItDown;
using Typedown.WinUI.Utilities;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // Portable build: finding the user's Python and MarkItDown, showing what was found on the Convert
    // page, letting the user point at a python.exe, and (if allowed) installing MarkItDown for the
    // user's account with pip --user. Nothing here changes PATH, the registry or anything outside
    // the user's own Python.
    public sealed partial class MainWindow
    {
        // The extras for the formats Caret offers (MarkItDownFormats). Lighter than [all], which also
        // pulls in audio transcription and Azure clients.
        private const string MarkItDownPackage = "markitdown[docx,pptx,xlsx,xls,pdf,outlook]";

        private Task<PythonInfo> pythonDetection;
        private bool installingMarkItDown;

        // Cached for the session; force re-runs detection (after an install, or "Detect again").
        private Task<PythonInfo> DetectPythonAsync(bool force = false)
        {
            if (force || pythonDetection == null)
                pythonDetection = PythonLocator.FindAsync(settings.PythonPath);
            return pythonDetection;
        }

        private async Task RefreshPythonStatusAsync(bool force = false)
        {
            ConvertEngineProgress.IsActive = true;
            ConvertEngineStatusText.Text = Locale.GetString("EngineChecking");
            ConvertEngineDetailText.Visibility = Visibility.Collapsed;
            ConvertInstallMarkItDownButton.Visibility = Visibility.Collapsed;
            PythonInfo python;
            try { python = await DetectPythonAsync(force); }
            finally { ConvertEngineProgress.IsActive = false; }
            ShowPythonStatus(python);
        }

        private void ShowPythonStatus(PythonInfo python)
        {
            if (python?.HasMarkItDown == true)
            {
                ConvertEngineStatusText.Text = Locale.Format("EngineReady", python.MarkItDownVersion, python.PythonVersion);
                ConvertEngineDetailText.Text = python.Executable;
                Log($"MarkItDown: {python.MarkItDownVersion} in {python.Executable}");
            }
            else if (python != null)
            {
                ConvertEngineStatusText.Text = Locale.Format("EngineNoMarkItDown", python.PythonVersion);
                ConvertEngineDetailText.Text = python.Executable;
                ConvertInstallMarkItDownButton.Visibility = Visibility.Visible;
                Log($"MarkItDown: not installed in {python.Executable}");
            }
            else
            {
                ConvertEngineStatusText.Text = Locale.GetString("EngineNoPython");
                ConvertEngineDetailText.Text = "";
                Log("MarkItDown: no Python found");
            }
            ConvertEngineDetailText.Visibility = string.IsNullOrEmpty(ConvertEngineDetailText.Text) ? Visibility.Collapsed : Visibility.Visible;
            ConvertEngineAutomaticButton.Visibility = string.IsNullOrEmpty(settings.PythonPath) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void ConvertChoosePython_Click(object sender, RoutedEventArgs e) => await ChoosePythonAsync();

        private async void ConvertDetectPython_Click(object sender, RoutedEventArgs e) => await RefreshPythonStatusAsync(force: true);

        private async void ConvertUseAutomaticPython_Click(object sender, RoutedEventArgs e)
        {
            settings.PythonPath = "";
            await RefreshPythonStatusAsync(force: true);
        }

        private async void ConvertInstallMarkItDown_Click(object sender, RoutedEventArgs e)
        {
            var python = await DetectPythonAsync();
            if (python != null && !python.HasMarkItDown) await InstallMarkItDownAsync(python);
        }

        private async Task<bool> ChoosePythonAsync()
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".exe");
            var picked = await picker.PickSingleFileAsync();
            if (picked == null) return false;
            var info = await PythonLocator.ProbeAsync(picked.Path);
            if (info == null)
            {
                await ShowErrorDialog(Locale.GetString("EngineNotAPython"), Locale.GetString("EngineNotAPythonDetail"));
                return false;
            }
            settings.PythonPath = info.Executable;
            pythonDetection = Task.FromResult(info);
            if (convertPageReady) ShowPythonStatus(info);
            return true;
        }

        // The Python to convert with, once MarkItDown is ready in it. Walks the user through whatever
        // is missing first; null when it still isn't ready (the dialogs have said why).
        private async Task<PythonInfo> EnsureMarkItDownAsync()
        {
            var python = await DetectPythonAsync();
            if (python?.HasMarkItDown == true) return python;
            // Maybe installed since the last look (in a terminal, say).
            python = await DetectPythonAsync(force: true);
            if (convertPageReady) ShowPythonStatus(python);
            if (python?.HasMarkItDown == true) return python;

            if (python == null)
            {
                var noPython = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = Locale.GetString("PythonNotFound"),
                    Content = Locale.GetString("EngineNoPythonDetail"),
                    PrimaryButtonText = Locale.GetString("EngineChoosePython"),
                    SecondaryButtonText = Locale.GetString("OpenPythonOrg"),
                    CloseButtonText = Locale.GetString("NotNow"),
                    DefaultButton = ContentDialogButton.Primary,
                };
                switch (await noPython.ShowAsync())
                {
                    case ContentDialogResult.Primary:
                        if (!await ChoosePythonAsync()) return null;
                        python = await DetectPythonAsync();
                        if (python?.HasMarkItDown == true) return python;
                        break;
                    case ContentDialogResult.Secondary:
                        await Launcher.LaunchUriAsync(new Uri("https://www.python.org/downloads/windows/"));
                        return null;
                    default:
                        return null;
                }
            }
            return python != null && await InstallMarkItDownAsync(python) ? await DetectPythonAsync() : null;
        }

        // pip install --user into the given Python. Only the user's own site-packages changes; no
        // administrator rights, no PATH edit. An organization can switch this off
        // (Config.PolicyDisablesMarkItDownInstall), and then Caret shows the command instead.
        private async Task<bool> InstallMarkItDownAsync(PythonInfo python)
        {
            if (installingMarkItDown) return false;
            var command = $"\"{python.Executable}\" -m pip install --user \"{MarkItDownPackage}\"";
            if (Config.PolicyDisablesMarkItDownInstall)
            {
                await ShowCommandDialog(Locale.GetString("MarkItDownNotFound"), Locale.GetString("EngineManualInstall"), command);
                return false;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Locale.GetString("MarkItDownNotFound"),
                Content = Locale.Format("EngineInstallPrompt", python.PythonVersion),
                PrimaryButtonText = Locale.GetString("Install"),
                CloseButtonText = Locale.GetString("NotNow"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return false;

            installingMarkItDown = true;
            if (convertPageReady)
            {
                ConvertEngineProgress.IsActive = true;
                ConvertEngineStatusText.Text = Locale.GetString("InstallingMarkItDown");
                ConvertInstallMarkItDownButton.IsEnabled = false;
            }
            Log($"MarkItDown: installing with {command}");
            try
            {
                var (exitCode, stdout, stderr, timedOut) = await ProcessRunner.RunAsync(python.Executable,
                    new[] { "-m", "pip", "install", "--user", "--disable-pip-version-check", MarkItDownPackage }, 600_000);
                if (timedOut || exitCode != 0)
                {
                    var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    Log($"MarkItDown: pip install failed (exit={exitCode}, timedOut={timedOut}): {output}");
                    var reason = timedOut ? Locale.GetString("MarkItDownInstallTimedOut")
                        : exitCode == null ? Locale.GetString("EngineNotAPythonDetail")
                        : string.Join("\n", (output ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(6));
                    await ShowCommandDialog(Locale.GetString("MarkItDownInstallFailed"),
                        (string.IsNullOrWhiteSpace(reason) ? "" : reason + "\n\n") + Locale.GetString("EngineManualInstall"), command);
                    return false;
                }
                Log("MarkItDown: install finished");
            }
            finally
            {
                installingMarkItDown = false;
                if (convertPageReady)
                {
                    ConvertEngineProgress.IsActive = false;
                    ConvertInstallMarkItDownButton.IsEnabled = true;
                }
            }

            var after = await DetectPythonAsync(force: true);
            if (convertPageReady) ShowPythonStatus(after);
            return after?.HasMarkItDown == true;
        }

        // A message plus a command the user can select and copy.
        private async Task ShowCommandDialog(string title, string message, string command)
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBox
            {
                Text = command,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = title,
                Content = panel,
                PrimaryButtonText = Locale.GetString("EngineCopyCommand"),
                CloseButtonText = Locale.GetString("OK"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) CopyTextToClipboard(command);
        }

        // File > Import Document as Markdown: one file, converted by MarkItDown and opened as a new,
        // unsaved note.
        private async void ImportMarkItDownMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var ext in MarkItDownFormats.Extensions) picker.FileTypeFilter.Add(ext);
            var pickedFile = await picker.PickSingleFileAsync();
            if (pickedFile == null) return;
            if (!await ConfirmDiscardChangesIfNeeded()) return;

            var python = await EnsureMarkItDownAsync();
            if (python == null) return;

            ImportMarkItDownMenuItem.IsEnabled = false;
            // A transient status in the title; UpdateTitle() in finally puts the real one back.
            TitleTextBlock.Text = Locale.GetString("ConvertingWithMarkItDown");
            Title = TitleTextBlock.Text;
            try
            {
                Log($"MarkItDown: importing {pickedFile.Path}");
                using var worker = await MarkItDownWorker.StartAsync(python.Executable);
                var result = await worker.ConvertAsync(pickedFile.Path, ConvertFileTimeoutMs);
                if (!result.Ok || string.IsNullOrWhiteSpace(result.Markdown))
                {
                    Log($"MarkItDown: import failed {pickedFile.Path}: {result.ErrorKind}: {result.Error}");
                    await ShowErrorDialog(Locale.GetString("ImportFailed"),
                        result.Ok ? Locale.GetString("ConvertNoContent") : FriendlyMarkItDownError(result.ErrorKind, result.Error));
                    return;
                }
                file.NewFile();
                file.ApplyRecoveredBackup(result.Markdown.Trim() + "\n");
                Log($"MarkItDown: imported {pickedFile.Path} ({result.Markdown.Length} chars)");
            }
            catch (TimeoutException)
            {
                await ShowErrorDialog(Locale.GetString("ImportFailed"), Locale.GetString("MarkItDownTimedOut"));
            }
            catch (MarkItDownStartException ex)
            {
                Log($"MarkItDown: couldn't start for import: {ex.Message}");
                await ShowErrorDialog(Locale.GetString("ImportFailed"), Locale.Format("ConvertFailed", LastLine(ex.Message)));
            }
            finally
            {
                UpdateTitle();
                ImportMarkItDownMenuItem.IsEnabled = true;
            }
        }
    }
}
