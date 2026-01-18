using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gtk;

public class CbrToCbzConverter : Window
{
    private Label statusLabel;
    private CheckButton deleteOriginalCheckbox;
    private Button startButton;
    private Button deleteOriginalsButton;
    private TextView logTextView;
    private TextBuffer logBuffer;
    private TreeView fileTreeView;
    private ListStore fileListStore;
    private List<string> queuedFiles;
    private List<string> successfulConversions;
    private int maxThreads;
    private object logLock = new object();

    public CbrToCbzConverter() : base("CBR to CBZ Converter")
    {
        SetDefaultSize(700, 600);
        SetPosition(WindowPosition.Center);
        DeleteEvent += OnDeleteEvent;

        queuedFiles = new List<string>();
        successfulConversions = new List<string>();
        maxThreads = Environment.ProcessorCount;

        // Create main vertical box
        VBox vbox = new VBox(false, 10);
        vbox.BorderWidth = 15;

        // Title label
        Label titleLabel = new Label();
        titleLabel.Markup = "<span size='large' weight='bold'>CBR to CBZ Converter</span>";
        vbox.PackStart(titleLabel, false, false, 5);

        // Info label
        Label infoLabel = new Label("Using " + maxThreads + " threads for parallel processing");
        infoLabel.Xalign = 0;
        vbox.PackStart(infoLabel, false, false, 0);

        // Drop zone frame
        Frame dropFrame = new Frame("Drop CBR Files Here");
        EventBox dropZone = new EventBox();
        
        Label dropLabel = new Label("Drag and drop .cbr files here");
        dropLabel.MarginTop = 40;
        dropLabel.MarginBottom = 40;
        dropLabel.MarginStart = 20;
        dropLabel.MarginEnd = 20;
        dropZone.Add(dropLabel);
        dropFrame.Add(dropZone);
        
        // Setup drag and drop
        Gtk.Drag.DestSet(dropZone, DestDefaults.All, 
            new TargetEntry[] {
                new TargetEntry("text/uri-list", 0, 0),
                new TargetEntry("text/plain", 0, 1)
            }, 
            Gdk.DragAction.Copy);
        
        dropZone.DragDataReceived += OnDragDataReceived;
        
        vbox.PackStart(dropFrame, false, false, 5);

        // File list with scrolled window
        Frame fileListFrame = new Frame("Queued Files");
        ScrolledWindow fileScrolledWindow = new ScrolledWindow();
        fileScrolledWindow.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        fileScrolledWindow.SetSizeRequest(-1, 150);
        
        // Create tree view with columns
        fileListStore = new ListStore(typeof(string), typeof(string));
        fileTreeView = new TreeView(fileListStore);
        
        TreeViewColumn fileColumn = new TreeViewColumn();
        fileColumn.Title = "Filename";
        CellRendererText fileCell = new CellRendererText();
        fileColumn.PackStart(fileCell, true);
        fileColumn.AddAttribute(fileCell, "text", 0);
        fileTreeView.AppendColumn(fileColumn);
        
        TreeViewColumn pathColumn = new TreeViewColumn();
        pathColumn.Title = "Full Path";
        CellRendererText pathCell = new CellRendererText();
        pathColumn.PackStart(pathCell, true);
        pathColumn.AddAttribute(pathCell, "text", 1);
        fileTreeView.AppendColumn(pathColumn);
        
        fileScrolledWindow.Add(fileTreeView);
        fileListFrame.Add(fileScrolledWindow);
        vbox.PackStart(fileListFrame, true, true, 5);

        // Button box
        HBox buttonBox = new HBox(false, 10);
        
        startButton = new Button("Start Conversion");
        startButton.Sensitive = false;
        startButton.Clicked += OnStartConversion;
        buttonBox.PackStart(startButton, true, true, 0);
        
        Button clearButton = new Button("Clear Queue");
        clearButton.Clicked += OnClearQueue;
        buttonBox.PackStart(clearButton, true, true, 0);
        
        vbox.PackStart(buttonBox, false, false, 5);

        // Delete original checkbox
        deleteOriginalCheckbox = new CheckButton("Delete original CBR files after successful conversion");
        vbox.PackStart(deleteOriginalCheckbox, false, false, 5);

        // Delete button (initially hidden)
        deleteOriginalsButton = new Button("Delete Original Files Now");
        deleteOriginalsButton.Sensitive = false;
        deleteOriginalsButton.Clicked += OnDeleteOriginals;
        vbox.PackStart(deleteOriginalsButton, false, false, 5);

        // Status label
        statusLabel = new Label("Ready - Drop files to begin");
        statusLabel.Xalign = 0;
        vbox.PackStart(statusLabel, false, false, 5);

        // Log text view with scrolled window
        Frame logFrame = new Frame("Conversion Log");
        ScrolledWindow scrolledWindow = new ScrolledWindow();
        scrolledWindow.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scrolledWindow.SetSizeRequest(-1, 200);
        
        logTextView = new TextView();
        logTextView.Editable = false;
        logTextView.WrapMode = WrapMode.Word;
        logBuffer = logTextView.Buffer;
        scrolledWindow.Add(logTextView);
        logFrame.Add(scrolledWindow);
        
        vbox.PackStart(logFrame, true, true, 5);

        Add(vbox);
        ShowAll();
    }

    private void OnDragDataReceived(object o, DragDataReceivedArgs args)
    {
        string data = System.Text.Encoding.UTF8.GetString(args.SelectionData.Data);
        string[] uris = data.Split('\n');

        int addedCount = 0;
        foreach (string uri in uris)
        {
            string cleanUri = uri.Trim('\r', '\n', ' ');
            if (string.IsNullOrEmpty(cleanUri))
                continue;

            // Remove file:// prefix
            if (cleanUri.StartsWith("file://"))
                cleanUri = cleanUri.Substring(7);

            // URL decode
            cleanUri = Uri.UnescapeDataString(cleanUri);

            if (File.Exists(cleanUri) && cleanUri.ToLower().EndsWith(".cbr"))
            {
                if (!queuedFiles.Contains(cleanUri))
                {
                    queuedFiles.Add(cleanUri);
                    string filename = System.IO.Path.GetFileName(cleanUri);
                    fileListStore.AppendValues(filename, cleanUri);
                    addedCount++;
                }
            }
        }

        if (addedCount > 0)
        {
            startButton.Sensitive = true;
            UpdateStatus(queuedFiles.Count + " file(s) queued");
            LogMessage("Added " + addedCount + " file(s) to queue");
        }

        Gtk.Drag.Finish(args.Context, true, false, args.Time);
    }

    private void OnClearQueue(object sender, EventArgs args)
    {
        queuedFiles.Clear();
        fileListStore.Clear();
        startButton.Sensitive = false;
        UpdateStatus("Queue cleared");
        LogMessage("Queue cleared");
    }

    private void OnStartConversion(object sender, EventArgs args)
    {
        startButton.Sensitive = false;
        deleteOriginalsButton.Sensitive = false;
        successfulConversions.Clear();
        
        LogMessage("========================================");
        LogMessage("Starting conversion of " + queuedFiles.Count + " file(s) using " + maxThreads + " threads");
        LogMessage("========================================");
        UpdateStatus("Converting...");

        List<string> filesToConvert = new List<string>(queuedFiles);
        
        Task.Run(() => {
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = maxThreads;
            
            Parallel.ForEach(filesToConvert, options, (cbrPath) => {
                ConvertCbrToCbz(cbrPath);
            });
            
            // After all conversions complete
            Gtk.Application.Invoke(delegate {
                OnConversionComplete();
            });
        });
    }

    private void OnConversionComplete()
    {
        LogMessage("========================================");
        LogMessage("Conversion complete: " + successfulConversions.Count + " of " + queuedFiles.Count + " files succeeded");
        LogMessage("========================================");
        
        UpdateStatus("Conversion complete - " + successfulConversions.Count + " successful");
        
        if (deleteOriginalCheckbox.Active && successfulConversions.Count > 0)
        {
            // Auto-delete if checkbox was checked
            DeleteOriginalFiles();
        }
        else if (successfulConversions.Count > 0)
        {
            deleteOriginalsButton.Sensitive = true;
        }
        
        startButton.Sensitive = true;
    }

    private void OnDeleteOriginals(object sender, EventArgs args)
    {
        DeleteOriginalFiles();
    }

    private void DeleteOriginalFiles()
    {
        int trashedCount = 0;
        LogMessage("Moving original files to trash...");
        
        foreach (string cbrPath in successfulConversions)
        {
            try
            {
                if (File.Exists(cbrPath))
                {
                    // Use gio trash to move file to trash instead of permanent deletion
                    ProcessStartInfo trashInfo = new ProcessStartInfo();
                    trashInfo.FileName = "gio";
                    trashInfo.Arguments = "trash \"" + cbrPath + "\"";
                    trashInfo.UseShellExecute = false;
                    trashInfo.RedirectStandardOutput = true;
                    trashInfo.RedirectStandardError = true;
                    trashInfo.CreateNoWindow = true;

                    Process trashProcess = Process.Start(trashInfo);
                    string errorOutput = trashProcess.StandardError.ReadToEnd();
                    trashProcess.WaitForExit();

                    if (trashProcess.ExitCode == 0)
                    {
                        trashedCount++;
                        LogMessage("Moved to trash: " + System.IO.Path.GetFileName(cbrPath));
                    }
                    else
                    {
                        LogMessage("ERROR moving to trash " + System.IO.Path.GetFileName(cbrPath) + ": " + errorOutput);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR moving to trash " + System.IO.Path.GetFileName(cbrPath) + ": " + ex.Message);
            }
        }
        
        LogMessage("Moved " + trashedCount + " original file(s) to trash");
        LogMessage("You can restore them from trash using: gio trash --list && gio trash --restore <URI>");
        deleteOriginalsButton.Sensitive = false;
    }

    private string DetectFileFormat(string filePath)
    {
        try
        {
            // Use 'file' command to detect actual format
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "file";
            psi.Arguments = "-b \"" + filePath + "\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            Process process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output;
        }
        catch
        {
            return "Unknown (file command not available)";
        }
    }

    private string GetFileMimeType(string filePath)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "file";
            psi.Arguments = "-b --mime-type \"" + filePath + "\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            Process process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output;
        }
        catch
        {
            return "unknown";
        }
    }

    private string GetFileHexSignature(string filePath)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[16];
                int bytesRead = fs.Read(buffer, 0, 16);
                string hex = "";
                for (int i = 0; i < bytesRead; i++)
                {
                    hex += buffer[i].ToString("X2") + " ";
                }
                return hex.Trim();
            }
        }
        catch
        {
            return "Unable to read";
        }
    }

private void ShowFileFormatAnalysis(string threadId, string cbrPath, string extractError)
{
    LogMessage(threadId + " ===== FILE FORMAT ANALYSIS =====");
    string fileType = DetectFileFormat(cbrPath);
    string mimeType = GetFileMimeType(cbrPath);
    string hexSignature = GetFileHexSignature(cbrPath);
    FileInfo fi = new FileInfo(cbrPath);
    
    LogMessage(threadId + " File: " + System.IO.Path.GetFileName(cbrPath));
    LogMessage(threadId + " Size: " + (fi.Length / 1024) + " KB");
    LogMessage(threadId + " Detected Type: " + fileType);
    LogMessage(threadId + " MIME Type: " + mimeType);
    LogMessage(threadId + " Hex Signature: " + hexSignature);
    
    // Analyze the signature
    if (hexSignature.StartsWith("00 00 00 00 00 00 00 00"))
    {
        LogMessage(threadId + " Analysis: FILE IS COMPLETELY ZEROED - likely disk/download corruption");
    }
    else if (hexSignature.StartsWith("52 61 72 21 1A 07"))
    {
        LogMessage(threadId + " Analysis: Valid RAR signature but extraction failed");
        if (extractError.Contains("password") || extractError.Contains("encrypted"))
        {
            LogMessage(threadId + " Reason: PASSWORD PROTECTED / ENCRYPTED");
        }
        else if (extractError.Contains("checksum") || extractError.Contains("CRC"))
        {
            LogMessage(threadId + " Reason: CRC/CHECKSUM ERRORS (file corruption)");
        }
        else
        {
            LogMessage(threadId + " Reason: Unknown - possibly corrupted header");
        }
    }
    else if (hexSignature.StartsWith("50 4B 03 04"))
    {
        LogMessage(threadId + " Analysis: Valid ZIP signature but extraction failed");
    }
    
    if (!string.IsNullOrEmpty(extractError))
        LogMessage(threadId + " Extraction Error: " + extractError.Trim());
    
    LogMessage(threadId + " ");
    LogMessage(threadId + " Common format signatures:");
    LogMessage(threadId + "   RAR: 52 61 72 21 1A 07");
    LogMessage(threadId + "   ZIP: 50 4B 03 04");
    LogMessage(threadId + "   7z:  37 7A BC AF 27 1C");
    LogMessage(threadId + " ");
    LogMessage(threadId + " Suggestion: This file may be corrupted, encrypted,");
    LogMessage(threadId + "             or have a non-standard wrapper.");
    LogMessage(threadId + " Try re-downloading from original source");
    LogMessage(threadId + " ================================");
}

    private bool IsSystemFile(string fileName)
    {
        // Filter out system and metadata files
        return fileName.StartsWith("._") ||           // macOS AppleDouble files
               fileName.StartsWith(".") ||            // All hidden files
               fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    private int CountFilesInDirectory(string directory)
    {
        try
        {
            string[] allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            int fileCount = 0;
            
            foreach (string file in allFiles)
            {
                string fileName = System.IO.Path.GetFileName(file);
                
                // Skip system files using shared filter method
                if (!IsSystemFile(fileName))
                {
                    fileCount++;
                }
            }
            
            return fileCount;
        }
        catch (Exception ex)
        {
            LogMessage("ERROR counting files in directory: " + ex.Message);
            return -1;
        }
    }

    private int CountFilesInArchive(string archivePath, bool isRar)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            
            if (isRar)
            {
                psi.FileName = "lsar";
                psi.Arguments = "\"" + archivePath + "\"";
            }
            else
            {
                psi.FileName = "unzip";
                psi.Arguments = "-l \"" + archivePath + "\"";
            }
            
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            Process process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int fileCount = 0;
                bool inFileList = false;
                
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    
                    if (isRar)
                    {
                        // lsar output format - skip header lines
                        if (trimmed.StartsWith("----") || trimmed.Contains("Archive:") || 
                            trimmed.Contains("Flags") || string.IsNullOrEmpty(trimmed))
                            continue;
                        
                        // Lines with file entries have specific format
                        // Skip directory entries (end with /)
                        if (!trimmed.EndsWith("/") && trimmed.Length > 0)
                        {
                            // Check if line contains file info (has numbers indicating size)
                            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\d+\s+\d{4}-\d{2}-\d{2}") ||
                                System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\s+\d+\s+"))
                            {
                                // Extract filename and filter system files
                                string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0)
                                {
                                    string filename = parts[parts.Length - 1];
                                    if (!IsSystemFile(filename))
                                    {
                                        fileCount++;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // unzip -l output format
                        // File list starts after "------" and ends before final summary
                        if (trimmed.StartsWith("------"))
                        {
                            inFileList = !inFileList;
                            continue;
                        }
                        
                        if (inFileList && !string.IsNullOrEmpty(trimmed))
                        {
                            // Extract filename from end of line
                            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                string filename = parts[parts.Length - 1];
                                
                                // Skip directory entries and system files
                                if (!filename.EndsWith("/") && !IsSystemFile(filename))
                                {
                                    fileCount++;
                                }
                            }
                        }
                    }
                }
                
                return fileCount;
            }
        }
        catch (Exception ex)
        {
            LogMessage("ERROR counting files in archive: " + ex.Message);
        }
        
        return -1;
    }

    private void ConvertCbrToCbz(string cbrPath)
    {
        string threadId = "[Thread " + Thread.CurrentThread.ManagedThreadId + "]";
        
        try
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(cbrPath);
            string directory = System.IO.Path.GetDirectoryName(cbrPath);
            string tempDir = System.IO.Path.Combine(directory, fileName + "_temp_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cbzPath = System.IO.Path.Combine(directory, fileName + ".cbz");

            LogMessage(threadId + " Processing: " + System.IO.Path.GetFileName(cbrPath));

            // Check file size - skip empty/placeholder files
            FileInfo fileInfo = new FileInfo(cbrPath);
            if (fileInfo.Length < 1024)  // Less than 1KB
            {
                LogMessage(threadId + " SKIPPED: File too small (" + fileInfo.Length + " bytes) - likely a failed download placeholder");
                return;
            }

            // Extract CBR (try multiple methods)
            LogMessage(threadId + " Extracting...");
            Directory.CreateDirectory(tempDir);

            bool extractSuccess = false;
            string extractError = "";

            // Try 1: unar (handles most formats)
            ProcessStartInfo extractInfo = new ProcessStartInfo();
            extractInfo.FileName = "unar";
            extractInfo.Arguments = "-o \"" + tempDir + "\" \"" + cbrPath + "\"";
            extractInfo.UseShellExecute = false;
            extractInfo.RedirectStandardOutput = true;
            extractInfo.RedirectStandardError = true;
            extractInfo.CreateNoWindow = true;

            Process extractProcess = Process.Start(extractInfo);
            extractError = extractProcess.StandardError.ReadToEnd();
            extractProcess.WaitForExit();

            if (extractProcess.ExitCode == 0)
            {
                extractSuccess = true;
            }
            else
            {
                // Check if it's a RAR file
                string mimeType = GetFileMimeType(cbrPath);
                
                if (mimeType.Contains("rar"))
                {
                    // Try 2: unrar with lenient extraction (keep broken files)
                    LogMessage(threadId + " unar failed, trying unrar...");
                    
                    try
                    {
                        ProcessStartInfo unrarInfo = new ProcessStartInfo();
                        unrarInfo.FileName = "unrar";
                        unrarInfo.Arguments = "x -y -kb \"" + cbrPath + "\" \"" + tempDir + "\"";
                        unrarInfo.UseShellExecute = false;
                        unrarInfo.RedirectStandardOutput = true;
                        unrarInfo.RedirectStandardError = true;
                        unrarInfo.CreateNoWindow = true;

                        Process unrarProcess = Process.Start(unrarInfo);
                        string unrarError = unrarProcess.StandardError.ReadToEnd();
                        unrarProcess.WaitForExit();
                        
                        // Accept exit codes 0 (success), 1 (warning), 3 (CRC error but extracted)
                        if (unrarProcess.ExitCode == 0 || unrarProcess.ExitCode == 1 || unrarProcess.ExitCode == 3)
                        {
                            // Check if any files were actually extracted
                            string[] extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                            if (extractedFiles.Length > 0)
                            {
                                extractSuccess = true;
                                if (unrarProcess.ExitCode == 3)
                                {
                                    LogMessage(threadId + " Extracted with CRC errors (some pages may be corrupted)");
                                }
                                else
                                {
                                    LogMessage(threadId + " Successfully extracted using unrar");
                                }
                            }
                            else
                            {
                                extractError = unrarError;
                            }
                        }
                        else
                        {
                            extractError = unrarError;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage(threadId + " unrar error: " + ex.Message);
                    }
                }
                
                if (!extractSuccess)
                {
                    // Try 3: 7z (good for many formats including RAR)
                    LogMessage(threadId + " trying 7z...");
                    
                    try
                    {
                        ProcessStartInfo sevenzInfo = new ProcessStartInfo();
                        sevenzInfo.FileName = "7z";
                        sevenzInfo.Arguments = "x -o\"" + tempDir + "\" -y \"" + cbrPath + "\"";
                        sevenzInfo.UseShellExecute = false;
                        sevenzInfo.RedirectStandardOutput = true;
                        sevenzInfo.RedirectStandardError = true;
                        sevenzInfo.CreateNoWindow = true;

                        Process sevenzProcess = Process.Start(sevenzInfo);
                        sevenzProcess.WaitForExit();
                        
                        if (sevenzProcess.ExitCode == 0)
                        {
                            extractSuccess = true;
                            LogMessage(threadId + " Successfully extracted using 7z");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage(threadId + " 7z error: " + ex.Message);
                    }
                }
                
                if (!extractSuccess)
                {
                    // Try 4: unzip (for ZIP files)
                    if (mimeType.Contains("zip"))
                    {
                        LogMessage(threadId + " trying unzip...");
                        
                        try
                        {
                            ProcessStartInfo unzipInfo = new ProcessStartInfo();
                            unzipInfo.FileName = "unzip";
                            unzipInfo.Arguments = "-q -o \"" + cbrPath + "\" -d \"" + tempDir + "\"";
                            unzipInfo.UseShellExecute = false;
                            unzipInfo.RedirectStandardOutput = true;
                            unzipInfo.RedirectStandardError = true;
                            unzipInfo.CreateNoWindow = true;

                            Process unzipProcess = Process.Start(unzipInfo);
                            unzipProcess.WaitForExit();
                            
                            if (unzipProcess.ExitCode == 0)
                            {
                                extractSuccess = true;
                                LogMessage(threadId + " Successfully extracted using unzip");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage(threadId + " unzip error: " + ex.Message);
                        }
                    }
                }
            }

            if (!extractSuccess)
            {
                LogMessage(threadId + " ERROR: All extraction methods failed");
                ShowFileFormatAnalysis(threadId, cbrPath, extractError);
                Directory.Delete(tempDir, true);
                return;
            }

            // Determine working directory for zip (handle unar subdirectory)
            string zipWorkingDir = tempDir;
            string[] subdirs = Directory.GetDirectories(tempDir);
            string[] rootFiles = Directory.GetFiles(tempDir);
            if (subdirs.Length == 1 && rootFiles.Length == 0)
            {
                zipWorkingDir = subdirs[0];
                LogMessage(threadId + " Using subdirectory: " + System.IO.Path.GetFileName(zipWorkingDir));
            }

            // Count extracted files (excluding system files) from the working directory
            LogMessage(threadId + " Counting extracted pages...");
            int originalFileCount = CountFilesInDirectory(zipWorkingDir);
            
            if (originalFileCount <= 0)
            {
                LogMessage(threadId + " ERROR: No files found after extraction");
                Directory.Delete(tempDir, true);
                return;
            }
            
            LogMessage(threadId + " Found " + originalFileCount + " page(s)");

            // Delete system files from working directory before zipping
            LogMessage(threadId + " Cleaning system files...");
            string[] allFiles = Directory.GetFiles(zipWorkingDir, "*", SearchOption.AllDirectories);
            int cleanedCount = 0;
            foreach (string file in allFiles)
            {
                string filename = System.IO.Path.GetFileName(file);
                if (IsSystemFile(filename))
                {
                    File.Delete(file);
                    cleanedCount++;
                }
            }
            if (cleanedCount > 0)
            {
                LogMessage(threadId + " Removed " + cleanedCount + " system file(s)");
            }

            // Create CBZ (ZIP archive) from the working directory
            LogMessage(threadId + " Creating CBZ...");
            ProcessStartInfo zipInfo = new ProcessStartInfo();
            zipInfo.FileName = "zip";
            zipInfo.Arguments = "-r -q \"" + cbzPath + "\" . -x \".*\" \"*/.*\"";
            zipInfo.WorkingDirectory = zipWorkingDir;
            zipInfo.UseShellExecute = false;
            zipInfo.RedirectStandardOutput = true;
            zipInfo.RedirectStandardError = true;
            zipInfo.CreateNoWindow = true;

            Process zipProcess = Process.Start(zipInfo);
            zipProcess.WaitForExit();

            if (zipProcess.ExitCode != 0)
            {
                LogMessage(threadId + " ERROR: Failed to create " + System.IO.Path.GetFileName(cbzPath));
                Directory.Delete(tempDir, true);
                return;
            }

            // Verify file count in CBZ
            LogMessage(threadId + " Verifying page count...");
            int newFileCount = CountFilesInArchive(cbzPath, false);
            
            if (newFileCount != originalFileCount)
            {
                LogMessage(threadId + " ERROR: Page count mismatch! Extracted " + originalFileCount + " pages but CBZ has " + newFileCount + " pages");
                File.Delete(cbzPath);
                Directory.Delete(tempDir, true);
                return;
            }

            // Clean up temp directory
            Directory.Delete(tempDir, true);

            LogMessage(threadId + " SUCCESS: " + System.IO.Path.GetFileName(cbzPath) + " (verified " + newFileCount + " pages)");
            
            lock(successfulConversions)
            {
                successfulConversions.Add(cbrPath);
            }
        }
        catch (Exception ex)
        {
            LogMessage(threadId + " ERROR: " + ex.Message);
        }
    }

    private void UpdateStatus(string message)
    {
        Gtk.Application.Invoke(delegate {
            statusLabel.Text = message;
        });
    }

    private void LogMessage(string message)
    {
        Gtk.Application.Invoke(delegate {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            TextIter iter = logBuffer.EndIter;
            logBuffer.Insert(ref iter, "[" + timestamp + "] " + message + "\n");
            
            // Auto-scroll to bottom
            TextMark endMark = logBuffer.CreateMark(null, logBuffer.EndIter, false);
            logTextView.ScrollToMark(endMark, 0.0, true, 0.0, 1.0);
        });
    }

    private void OnDeleteEvent(object sender, DeleteEventArgs args)
    {
        Application.Quit();
    }

    public static void Main()
    {
        Application.Init();
        new CbrToCbzConverter();
        Application.Run();
    }
}
