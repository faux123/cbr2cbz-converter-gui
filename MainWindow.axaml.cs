using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CbrToCbz;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileEntry> _files = new();
    private readonly List<string> _successfulConversions = new();
    private readonly int _maxThreads = Environment.ProcessorCount;
    private CancellationTokenSource _cts = new();
    private readonly object _logLock = new();

    public MainWindow()
    {
        InitializeComponent();
        ThreadInfoLabel.Text = $"Using {_maxThreads} threads for parallel processing";
        FileGrid.ItemsSource = _files;
        SetupDragDrop();
    }

    // ── Drag and drop ──────────────────────────────────────────────────────────

    private void SetupDragDrop()
    {
        DragDrop.SetAllowDrop(DropZone, true);
        DropZone.AddHandler(DragDrop.DropEvent,       OnDrop);
        DropZone.AddHandler(DragDrop.DragEnterEvent,  OnDragEnter);
        DropZone.AddHandler(DragDrop.DragLeaveEvent,  OnDragLeave);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        DropZone.Background = Avalonia.Media.Brush.Parse("#F5E4C0");
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        DropZone.Background = Avalonia.Media.Brush.Parse("#FDF3E3");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Background = Avalonia.Media.Brush.Parse("#FDF3E3");
        var files = e.Data.GetFiles();
        if (files is null) return;

        int added = 0;
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (path is null) continue;
            if (!path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)) continue;
            if (_files.Any(x => x.FullPath == path)) continue;

            _files.Add(new FileEntry { Filename = Path.GetFileName(path), FullPath = path });
            added++;
        }

        if (added > 0)
        {
            StartButton.IsEnabled = true;
            UpdateStatus($"{_files.Count} file(s) queued");
            LogMessage($"Added {added} file(s) to queue");
        }
        e.Handled = true;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Add CBR Files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CBR Files") { Patterns = new[] { "*.cbr" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        };

        var results = await StorageProvider.OpenFilePickerAsync(options);
        int added = 0;
        foreach (var f in results)
        {
            var path = f.TryGetLocalPath();
            if (path is null || !File.Exists(path)) continue;
            if (!path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)) continue;
            if (_files.Any(x => x.FullPath == path)) continue;

            _files.Add(new FileEntry { Filename = Path.GetFileName(path), FullPath = path });
            added++;
        }

        if (added > 0)
        {
            StartButton.IsEnabled = true;
            UpdateStatus($"{_files.Count} file(s) queued");
            LogMessage($"Added {added} file(s) from file picker");
        }
    }

    private void OnClearQueue(object? sender, RoutedEventArgs e)
    {
        _files.Clear();
        StartButton.IsEnabled = false;
        UpdateStatus("Queue cleared");
        LogMessage("Queue cleared");
    }

    private void OnStartConversion(object? sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        CancelButton.IsVisible = true;
        DeleteOriginalsButton.IsEnabled = false;
        _successfulConversions.Clear();

        LogMessage("========================================");
        LogMessage($"Starting conversion of {_files.Count} file(s) using {_maxThreads} threads");
        LogMessage("========================================");
        UpdateStatus("Converting…");

        var filePaths = _files.Select(f => f.FullPath).ToList();
        var token = _cts.Token;

        Task.Run(() =>
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxThreads,
                CancellationToken = token
            };

            try
            {
                Parallel.ForEach(filePaths, options, path => ConvertCbrToCbz(path, token));
            }
            catch (OperationCanceledException) { }

            bool cancelled = token.IsCancellationRequested;
            Dispatcher.UIThread.InvokeAsync(() => OnConversionComplete(cancelled));
        });
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _cts.Cancel();

    private void OnConversionComplete(bool cancelled)
    {
        if (cancelled)
        {
            LogMessage("Conversion cancelled by user");
            _cts = new CancellationTokenSource();
        }

        LogMessage("========================================");
        LogMessage($"Conversion complete: {_successfulConversions.Count} of {_files.Count} succeeded");
        LogMessage("========================================");

        UpdateStatus(cancelled
            ? $"Cancelled — {_successfulConversions.Count} completed before cancel"
            : $"Done — {_successfulConversions.Count} converted successfully");

        CancelButton.IsVisible = false;
        StartButton.IsEnabled = true;

        if (!cancelled && DeleteOriginalCheckbox.IsChecked == true && _successfulConversions.Count > 0)
            DeleteOriginalFiles();
        else if (_successfulConversions.Count > 0)
            DeleteOriginalsButton.IsEnabled = true;
    }

    private void OnDeleteOriginals(object? sender, RoutedEventArgs e) => DeleteOriginalFiles();

    // ── File operations ───────────────────────────────────────────────────────

    private void DeleteOriginalFiles()
    {
        int trashed = 0;
        LogMessage("Moving original files to trash…");

        foreach (var path in _successfulConversions)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "gio",
                    Arguments = $"trash \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(info)!;
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    trashed++;
                    LogMessage($"Moved to trash: {Path.GetFileName(path)}");
                }
                else
                    LogMessage($"ERROR moving to trash {Path.GetFileName(path)}: {err}");
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR: {ex.Message}");
            }
        }

        LogMessage($"Moved {trashed} file(s) to trash");
        DeleteOriginalsButton.IsEnabled = false;
    }

    private void SetFileStatus(string filePath, string status)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = _files.FirstOrDefault(f => f.FullPath == filePath);
            if (entry != null) entry.Status = status;
        });
    }

    // ── Conversion pipeline ───────────────────────────────────────────────────

    private void ConvertCbrToCbz(string cbrPath, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        string threadId = $"[T{Thread.CurrentThread.ManagedThreadId}]";
        SetFileStatus(cbrPath, "Converting");

        string fileName  = Path.GetFileNameWithoutExtension(cbrPath);
        string directory = Path.GetDirectoryName(cbrPath)!;
        string tempDir   = Path.Combine(directory, $"{fileName}_temp_{Guid.NewGuid():N}"[..16]);
        string cbzPath   = Path.Combine(directory, $"{fileName}.cbz");

        try
        {
            var fi = new FileInfo(cbrPath);
            if (fi.Length < 1024)
            {
                LogMessage($"{threadId} SKIPPED: {Path.GetFileName(cbrPath)} too small ({fi.Length} bytes)");
                SetFileStatus(cbrPath, "Failed");
                return;
            }

            Directory.CreateDirectory(tempDir);

            // Try extraction: unar → unrar → 7z → unzip
            string extractError = "";
            bool extracted = false;

            if (!extracted) (extracted, extractError) = TryExtract("unar", $"-o \"{tempDir}\" \"{cbrPath}\"", tempDir);
            if (!extracted) (extracted, extractError) = TryExtract("unrar", $"x -y -kb \"{cbrPath}\" \"{tempDir}\"", tempDir);
            if (!extracted) (extracted, extractError) = TryExtract("7z",   $"x -o\"{tempDir}\" -y \"{cbrPath}\"", tempDir);
            if (!extracted) (extracted, extractError) = TryExtract("unzip", $"-q -o \"{cbrPath}\" -d \"{tempDir}\"", tempDir);

            if (!extracted)
            {
                ShowFileFormatAnalysis(threadId, cbrPath, extractError);
                SetFileStatus(cbrPath, "Failed");
                return;
            }

            // Collect image files, skip system files
            var imageFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => !IsSystemFile(Path.GetFileName(f)))
                .OrderBy(f => f)
                .ToList();

            if (imageFiles.Count == 0)
            {
                LogMessage($"{threadId} ERROR: No image files found after extraction");
                SetFileStatus(cbrPath, "Failed");
                return;
            }

            int beforeCount = imageFiles.Count;
            LogMessage($"{threadId} Extracted {beforeCount} files from {Path.GetFileName(cbrPath)}");

            // Create CBZ (zip) from temp dir
            string zipWorkDir = imageFiles.Select(Path.GetDirectoryName).GroupBy(d => d)
                .OrderByDescending(g => g.Count()).First().Key!;

            var zipInfo = new ProcessStartInfo
            {
                FileName = "zip",
                Arguments = $"-r -q \"{cbzPath}\" . -x \".*\" \"*/.*\"",
                WorkingDirectory = zipWorkDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var zipProc = Process.Start(zipInfo)!)
            {
                string zipErr = zipProc.StandardError.ReadToEnd();
                zipProc.WaitForExit();
                if (zipProc.ExitCode != 0)
                {
                    LogMessage($"{threadId} ERROR: Failed to create CBZ: {zipErr}");
                    SetFileStatus(cbrPath, "Failed");
                    return;
                }
            }

            // Verify page count
            int afterCount = CountFilesInDirectory(tempDir);
            if (afterCount < beforeCount * 0.9)
            {
                LogMessage($"{threadId} WARNING: Page count mismatch (before: {beforeCount}, after: {afterCount})");
                File.Delete(cbzPath);
                SetFileStatus(cbrPath, "Failed");
                return;
            }

            Directory.Delete(tempDir, true);
            LogMessage($"{threadId} SUCCESS: {Path.GetFileName(cbzPath)} ({afterCount} pages)");
            SetFileStatus(cbrPath, "Done");

            lock (_successfulConversions)
                _successfulConversions.Add(cbrPath);
        }
        catch (Exception ex)
        {
            LogMessage($"{threadId} ERROR: {ex.Message}");
            SetFileStatus(cbrPath, "Failed");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (bool success, string error) TryExtract(string tool, string args, string tempDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, $"{tool} not found");
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            bool hasFiles = Directory.Exists(tempDir) &&
                            Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories).Length > 0;
            return (proc.ExitCode == 0 && hasFiles, err);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static bool IsSystemFile(string name) =>
        name.StartsWith("._") || name.StartsWith(".") ||
        name.Equals("Thumbs.db",  StringComparison.OrdinalIgnoreCase) ||
        name.Equals(".DS_Store",  StringComparison.OrdinalIgnoreCase) ||
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    private static int CountFilesInDirectory(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Count(f => !IsSystemFile(Path.GetFileName(f)));
        }
        catch { return -1; }
    }

    private void ShowFileFormatAnalysis(string threadId, string cbrPath, string extractError)
    {
        LogMessage($"{threadId} ===== FILE FORMAT ANALYSIS =====");
        string fileType = RunCommand("file", $"-b \"{cbrPath}\"");
        string mimeType = RunCommand("file", $"-b --mime-type \"{cbrPath}\"");
        string hex      = GetHexSignature(cbrPath);
        var fi = new FileInfo(cbrPath);

        LogMessage($"{threadId} File: {Path.GetFileName(cbrPath)}  Size: {fi.Length / 1024} KB");
        LogMessage($"{threadId} Type: {fileType}  MIME: {mimeType}");
        LogMessage($"{threadId} Hex:  {hex}");

        if (hex.StartsWith("52 61 72 21")) LogMessage($"{threadId} Valid RAR signature — extraction failed");
        else if (hex.StartsWith("50 4B 03 04")) LogMessage($"{threadId} Valid ZIP signature — extraction failed");
        else if (hex.StartsWith("00 00 00 00")) LogMessage($"{threadId} File is zeroed — likely corrupt");

        if (!string.IsNullOrEmpty(extractError)) LogMessage($"{threadId} Error: {extractError.Trim()}");
        LogMessage($"{threadId} ================================");
    }

    private static string RunCommand(string cmd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd, Arguments = args,
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return o;
        }
        catch { return "unavailable"; }
    }

    private static string GetHexSignature(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var buf = new byte[16];
            int n = fs.Read(buf, 0, 16);
            return string.Join(" ", buf.Take(n).Select(b => b.ToString("X2")));
        }
        catch { return "unreadable"; }
    }

    private void UpdateStatus(string msg) =>
        Dispatcher.UIThread.InvokeAsync(() => StatusLabel.Text = msg);

    private void LogMessage(string msg)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string ts   = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{ts}] {msg}\n";
            LogTextBox.Text += line;
            LogScroll.ScrollToEnd();
        });
    }
}
