using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Services.MarkItDown;
using Typedown.WinUI.Utilities;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // The Convert to Markdown page. Files are picked, dropped, or found in a whole folder (and its
    // subfolders), converted by Microsoft MarkItDown in the user's own Python (MainWindow.MarkItDown.cs),
    // and written as .md files, each with its size and an estimate of how many AI tokens it takes.
    // Nothing is ever overwritten: an existing "report.md" makes the new one "report (2).md".
    public sealed partial class MainWindow
    {
        private readonly ObservableCollection<ConversionItem> conversions = new();
        private bool convertPageReady;
        private bool convertOptionsUpdating;
        private bool converting;

        // Big PDFs take a while; a file that takes longer than this is given up on.
        private const int ConvertFileTimeoutMs = 300_000;
        private static readonly string[] EmailExtensions = { ".msg", ".eml" };

        private void SetConvertPageVisible(bool visible)
        {
            if (visible && !convertPageReady) InitializeConvertPage();
            ConvertPage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializeConvertPage()
        {
            convertPageReady = true;
            ConvertResultsList.ItemsSource = conversions;
            convertOptionsUpdating = true;
            ConvertRedactToggle.IsOn = settings.ConvertEmailRedact;
            UpdateConvertOutputChoice();
            convertOptionsUpdating = false;
            _ = RefreshPythonStatusAsync();
        }

        // Opening a note (from the page's Open button, the folder tree, Recent…) returns to the editor.
        private void SetUpConvertPage() =>
            eventCenter.GetObservable<EditorEventArgs>("FileLoaded").Subscribe(_ =>
            {
                if (ConvertPage.Visibility == Visibility.Visible) CloseConvertPage();
            });

        private void CloseConvertPage()
        {
            SetConvertPageVisible(false);
            if (SelectedNavTag is "Convert" or "Emails") SelectNav("Home");
            // Back to writing. Without this, focus falls to the next control (the status bar's word
            // count button), which also pops up its tooltip.
            EditorView.Focus(FocusState.Programmatic);
        }

        private void HomeConvertButton_Click(object sender, RoutedEventArgs e) => ShowConvertPage("Convert");

        // The Home card and File menu entry for emails go straight to picking them.
        private async void HomeEmailsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertPage("Emails");
            await ChooseEmailsAsync();
        }

        private void ShowConvertPage(string tag)
        {
            if (!SelectNav(tag)) SetConvertPageVisible(true);
        }

        private void ConvertBack_Click(object sender, RoutedEventArgs e) => CloseConvertPage();

        // --- Options ---

        private void UpdateConvertOutputChoice()
        {
            var folder = settings.ConvertOutputFolder;
            var useFolder = !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
            ConvertOutputFolderItem.Content = useFolder ? Locale.Format("ConvertSaveToFolderPath", folder) : Locale.GetString("ConvertSaveToFolder");
            ConvertOutputComboBox.SelectedIndex = useFolder ? 1 : 0;
        }

        private async void ConvertOutputComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (convertOptionsUpdating) return;
            if (ConvertOutputComboBox.SelectedIndex == 0)
            {
                settings.ConvertOutputFolder = "";
                return;
            }
            var picked = await Win32FolderPicker.PickFolderAsync(WindowNative.GetWindowHandle(this));
            if (!string.IsNullOrEmpty(picked)) settings.ConvertOutputFolder = picked;
            convertOptionsUpdating = true;
            UpdateConvertOutputChoice();
            convertOptionsUpdating = false;
        }

        private void ConvertRedactToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!convertOptionsUpdating) settings.ConvertEmailRedact = ConvertRedactToggle.IsOn;
        }

        // --- Input ---

        private async void ConvertChooseFiles_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var ext in MarkItDownFormats.Extensions.Concat(MarkItDownFormats.LegacyOfficeExtensions)) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files?.Count > 0) await ConvertPathsAsync(files.Select(f => f.Path));
        }

        // Outlook mail: drag messages from Outlook to a folder (or save them as .msg), then pick them here.
        private async void ConvertChooseEmails_Click(object sender, RoutedEventArgs e) => await ChooseEmailsAsync();

        private async Task ChooseEmailsAsync()
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var ext in EmailExtensions) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files?.Count > 0) await ConvertPathsAsync(files.Select(f => f.Path));
        }

        private async void ConvertChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = await Win32FolderPicker.PickFolderAsync(WindowNative.GetWindowHandle(this));
            if (!string.IsNullOrEmpty(folder)) await ConvertPathsAsync(new[] { folder });
        }

        private void ConvertPage_DragOver(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Locale.GetString("ConvertDropCaption");
        }

        private async void ConvertPage_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferral = e.GetDeferral();
            List<string> paths;
            try { paths = (await e.DataView.GetStorageItemsAsync()).Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList(); }
            finally { deferral.Complete(); }
            await ConvertPathsAsync(paths);
        }

        // --- Conversion ---

        private async Task ConvertPathsAsync(IEnumerable<string> inputs)
        {
            // (source file, folder it was found under — for mirroring subfolders into an output folder)
            var jobs = new List<(string File, string Root)>();
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                {
                    IEnumerable<string> found;
                    try
                    {
                        // On a worker thread: a big dropped folder must not freeze the window
                        found = await Task.Run(() => Directory.EnumerateFiles(input, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                            .Where(f => MarkItDownFormats.IsSupported(f) || MarkItDownFormats.IsLegacyOffice(f))
                            // Office lock files ("~$report.docx").
                            .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList());
                    }
                    catch (Exception ex)
                    {
                        Log($"Convert: couldn't list {input}: {ex.Message}");
                        continue;
                    }
                    jobs.AddRange(found.Select(f => (f, input)));
                }
                else if (File.Exists(input)) jobs.Add((input, Path.GetDirectoryName(input)));
            }
            if (jobs.Count == 0) return;

            // The newest batch goes on top, in the order the files were given.
            var items = jobs.Select((j, index) =>
            {
                var (badge, color) = ConversionItem.BadgeFor(Path.GetExtension(j.File));
                var item = new ConversionItem
                {
                    SourcePath = j.File,
                    Name = Path.GetFileName(j.File),
                    Badge = badge,
                    BadgeBrush = new SolidColorBrush(color),
                    Detail = Locale.GetString("ConvertWaiting"),
                };
                conversions.Insert(index, item);
                return (Item: item, j.Root);
            }).ToList();
            ConvertSummaryPanel.Visibility = Visibility.Visible;
            UpdateConvertSummary();

            // One batch at a time; later drops queue behind it.
            while (converting) await Task.Delay(200);
            converting = true;
            MarkItDownWorker worker = null;
            try
            {
                var python = await EnsureMarkItDownAsync();
                if (python == null)
                {
                    foreach (var (item, _) in items) Fail(item, Locale.GetString("ConvertMarkItDownNotReady"));
                    return;
                }
                foreach (var (item, root) in items)
                {
                    worker = await ConvertOneAsync(item, root, python, worker);
                    UpdateConvertSummary();
                }
            }
            finally
            {
                worker?.Dispose();
                converting = false;
                UpdateConvertSummary();
            }
        }

        // Returns the worker to use for the next file: the same one, a restarted one after a crash or
        // timeout, or null if it couldn't be started.
        private async Task<MarkItDownWorker> ConvertOneAsync(ConversionItem item, string root, PythonInfo python, MarkItDownWorker worker)
        {
            var source = item.SourcePath;
            if (MarkItDownFormats.IsLegacyOffice(source))
            {
                Fail(item, Locale.GetString("ConvertLegacyFormat"));
                return worker;
            }
            if (!MarkItDownFormats.IsSupported(source))
            {
                Fail(item, Locale.GetString("ConvertUnsupported"));
                return worker;
            }

            item.IsConverting = true;
            item.Detail = Locale.GetString("ConvertConverting");
            try
            {
                item.SourceBytes = new FileInfo(source).Length;
                if (worker == null || !worker.IsAlive)
                {
                    worker?.Dispose();
                    worker = await MarkItDownWorker.StartAsync(python.Executable);
                    Log($"Convert: MarkItDown {worker.Version} started in {python.Executable}; email plugin: {(worker.HasEmailPlugin ? "yes" : worker.EmailPluginError)}");
                }
                var result = await worker.ConvertAsync(source, ConvertFileTimeoutMs, settings.ConvertEmailRedact);
                if (!result.Ok)
                {
                    Log($"Convert: MarkItDown failed {source}: {result.ErrorKind}: {result.Error}");
                    Fail(item, FriendlyMarkItDownError(result.ErrorKind, result.Error));
                    return worker;
                }
                var markdown = result.Markdown.Trim() + "\n";
                if (string.IsNullOrWhiteSpace(result.Markdown))
                {
                    Fail(item, Path.GetExtension(source).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                        ? Locale.GetString("ConvertPdfNoText")
                        : Locale.GetString("ConvertNoContent"));
                    return worker;
                }
                var outputPath = OutputPathFor(source, root);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                await File.WriteAllTextAsync(outputPath, markdown, new UTF8Encoding(false));

                item.OutputPath = outputPath;
                item.Markdown = markdown;
                item.MarkdownBytes = Encoding.UTF8.GetByteCount(markdown);
                item.Tokens = MarkItDownFormats.EstimateTokens(markdown);
                item.Succeeded = true;
                var detail = Locale.Format("ConvertResultDetail", FormatSize(item.SourceBytes), FormatSize(item.MarkdownBytes), item.Tokens.ToString("N0"));
                // Rounded down: 99.6% must not read "100% smaller".
                var saved = item.SourceBytes > 0 ? Math.Floor(100.0 * (item.SourceBytes - item.MarkdownBytes) / item.SourceBytes) : 0;
                if (saved >= 1)
                    detail += " · " + Locale.Format("ConvertSmaller", saved);
                if (EmailExtensions.Contains(Path.GetExtension(source).ToLowerInvariant()) && settings.ConvertEmailRedact && worker.HasEmailPlugin)
                    detail += " · " + Locale.GetString("ConvertPersonalDataMasked");
                item.Detail = detail;
                item.ActionsVisibility = Visibility.Visible;
                Log($"Convert: {source} -> {outputPath} ({item.SourceBytes} -> {item.MarkdownBytes} bytes, ~{item.Tokens} tokens)");
            }
            catch (TimeoutException)
            {
                Log($"Convert: timed out {source}");
                Fail(item, Locale.GetString("MarkItDownTimedOut"));
            }
            catch (MarkItDownStartException ex)
            {
                Log($"Convert: MarkItDown stopped on {source}: {ex.Message}");
                Fail(item, Locale.Format("ConvertFailed", LastLine(ex.Message)));
            }
            catch (Exception ex)
            {
                Log($"Convert: failed {source}: {ex}");
                Fail(item, FriendlyConversionError(ex));
            }
            finally
            {
                item.IsConverting = false;
            }
            return worker;
        }

        private static void Fail(ConversionItem item, string message)
        {
            item.Failed = true;
            item.IsConverting = false;
            item.Detail = message;
        }

        private static string FriendlyConversionError(Exception ex) => ex switch
        {
            UnauthorizedAccessException => Locale.GetString("ConvertNoWriteAccess"),
            FileNotFoundException or DirectoryNotFoundException => Locale.GetString("ConvertFileMissing"),
            IOException io when io.HResult == unchecked((int)0x80070020) => Locale.GetString("ConvertFileLocked"),
            _ => Locale.Format("ConvertFailed", ex.Message),
        };

        // MarkItDown reports the Python exception's type name and message.
        private static string FriendlyMarkItDownError(string kind, string error)
        {
            error ??= "";
            if (kind == "MissingDependencyException" || error.Contains("have not been installed", StringComparison.OrdinalIgnoreCase))
                return Locale.GetString("ConvertMissingDependency");
            return kind switch
            {
                "UnsupportedFormatException" => Locale.GetString("ConvertUnsupported"),
                "FileNotFoundError" => Locale.GetString("ConvertFileMissing"),
                "PermissionError" => Locale.GetString("ConvertFileLocked"),
                "BadZipFile" or "PackageNotFoundError" or "PdfReadError" or "PDFSyntaxError" => Locale.GetString("ConvertDamagedOrProtected"),
                _ => Locale.Format("ConvertFailed", LastLine(error)),
            };
        }

        // A Python traceback's useful part is its last line.
        private static string LastLine(string text) =>
            (text ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

        // Next to the original, or in the chosen folder (mirroring subfolders of a dropped folder).
        // Never overwrites: "report.md" → "report (2).md".
        private string OutputPathFor(string source, string root)
        {
            var folder = settings.ConvertOutputFolder;
            string directory;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                var relative = Path.GetRelativePath(root, Path.GetDirectoryName(source));
                directory = relative == "." || relative.StartsWith("..") ? folder : Path.Combine(folder, relative);
            }
            else directory = Path.GetDirectoryName(source);
            var name = Path.GetFileNameWithoutExtension(source);
            bool Taken(string candidate) => File.Exists(candidate);
            var path = Path.Combine(directory, name + ".md");
            if (!Taken(path)) return path;
            // "report.docx" and "report.pdf" side by side → "report.md" and "report (PDF).md".
            path = Path.Combine(directory, $"{name} ({Path.GetExtension(source).TrimStart('.').ToUpperInvariant()}).md");
            for (var n = 2; Taken(path); n++)
                path = Path.Combine(directory, $"{name} ({n}).md");
            return path;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString(bytes < 10 * 1024 ? "0.#" : "N0") + " " + Locale.GetString("UnitKB");
            return (bytes / (1024.0 * 1024)).ToString("0.#") + " " + Locale.GetString("UnitMB");
        }

        private void UpdateConvertSummary()
        {
            var done = conversions.Where(c => c.Succeeded).ToList();
            var failed = conversions.Count(c => c.Failed);
            var pending = conversions.Count(c => !c.Succeeded && !c.Failed);
            ConvertSummaryTitle.Text = pending > 0
                ? Locale.Format("ConvertSummaryWorking", conversions.Count - pending, conversions.Count)
                : Locale.Format(done.Count == 1 || (done.Count == 0 && Locale.CurrentLang == "fr") ? "ConvertSummaryOne" : "ConvertSummaryMany", done.Count);
            var detail = done.Count == 0 ? "" : Locale.Format("ConvertSummaryDetail",
                FormatSize(done.Sum(c => c.SourceBytes)), FormatSize(done.Sum(c => c.MarkdownBytes)), done.Sum(c => c.Tokens).ToString("N0"));
            if (failed > 0) detail = (detail + " " + Locale.Format("ConvertSummaryFailed", failed)).Trim();
            ConvertSummaryDetail.Text = detail;
            ConvertSummaryDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConvertCopyAllButton.IsEnabled = done.Count > 0;
        }

        // --- Results ---

        // Everything converted so far as one Markdown text — ready to paste into an AI assistant.
        private async void ConvertCopyAll_Click(object sender, RoutedEventArgs e)
        {
            var done = conversions.Where(c => c.Succeeded).Reverse().ToList();
            if (done.Count == 0) return;
            var text = done.Count == 1
                ? done[0].Markdown
                : string.Join("\n\n---\n\n", done.Select(c => $"<!-- {c.Name} -->\n\n{c.Markdown.TrimEnd()}")) + "\n";
            CopyTextToClipboard(text);
            ConvertCopyAllText.Text = Locale.GetString("ConvertCopied");
            await Task.Delay(1800);
            ConvertCopyAllText.Text = Locale.GetString("ConvertCopyAll");
        }

        private void ConvertCopyResult_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ConversionItem item && item.Succeeded) CopyTextToClipboard(item.Markdown);
        }

        private static void CopyTextToClipboard(string text)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }

        private async void ConvertOpenResult_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ConversionItem item && File.Exists(item.OutputPath))
                await OpenRecentFile(item.OutputPath);
        }

        private void ConvertRevealResult_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ConversionItem item && File.Exists(item.OutputPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.OutputPath}\"");
        }

        private void ConvertClear_Click(object sender, RoutedEventArgs e)
        {
            if (converting) return;
            conversions.Clear();
            ConvertSummaryPanel.Visibility = Visibility.Collapsed;
        }
    }
}
