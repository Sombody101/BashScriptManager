using Gtk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace ConsoleApp2
{
    public class Script
    {
        public string Title { get; set; }
        public string Content { get; set; }
    }

    public class ProcessResourceInfo
    {
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; } // in KB
        public DateTime LastUpdate { get; set; }
    }

    public class Interface
    {
        private Window window;
        private ListStore scriptStore;
        private List<Script> scripts;
        private Dictionary<string, Process> runningProcesses = new Dictionary<string, Process>();
        private Dictionary<string, ProcessResourceInfo> processResources = new Dictionary<string, ProcessResourceInfo>();
        private const string ConfigFile = "scripts.json";
        private bool isDisposing = false;
        private System.Threading.Timer resourceMonitorTimer;

        public Interface()
        {
            Application.Init();
            window = new Window("Bash Script Manager");
            window.SetDefaultSize(1300, 800);
            
            Gdk.Geometry hints = new Gdk.Geometry
            {
                MinWidth = 900,
                MinHeight = 600
            };
            window.SetGeometryHints(window, hints, Gdk.WindowHints.MinSize);

            // Main container
            var mainBox = new Box(Orientation.Horizontal, 10);
            var leftBox = new Box(Orientation.Vertical, 10);
            var rightBox = new Box(Orientation.Vertical, 10);
            


            // Left side: Title entry, text input, and file chooser
            var titleEntry = new Entry { PlaceholderText = "Enter script title" };
            var textView = new TextView();
            textView.WrapMode = WrapMode.Word;
            var scrolledWindow = new ScrolledWindow();
            scrolledWindow.SetSizeRequest(600, 400);
            scrolledWindow.Add(textView);

            var addButton = new Button("Add Script");
            var fileButton = new Button("Add from File");
            var inputBox = new Box(Orientation.Horizontal, 5);
            inputBox.PackStart(addButton, false, false, 0);
            inputBox.PackStart(fileButton, false, false, 0);

            var stopButton = new Button("Stop Process");
            stopButton.Clicked += (s, e) => ShowStopDialog();
            leftBox.PackStart(titleEntry, false, false, 0);
            leftBox.PackStart(scrolledWindow, true, true, 0);
            leftBox.PackStart(inputBox, false, false, 0);
            leftBox.PackStart(stopButton, false, false, 0);

            // Right side: Script list and Run All button
            scripts = LoadScripts();
            scriptStore = new ListStore(typeof(string), typeof(string), typeof(string)); // Title, Status, Resources
            foreach (var script in scripts)
            {
                scriptStore.AppendValues(script.Title, "⚫ Stopped", "");
            }

            var treeView = new TreeView(scriptStore);
            
            // Script Title column
            var titleColumn = new TreeViewColumn { Title = "Script Title" };
            var cellText = new CellRendererText();
            titleColumn.PackStart(cellText, true);
            titleColumn.AddAttribute(cellText, "text", 0);
            treeView.AppendColumn(titleColumn);

            // Status column
            var statusColumn = new TreeViewColumn { Title = "Status" };
            var cellStatus = new CellRendererText();
            statusColumn.PackStart(cellStatus, true);
            statusColumn.AddAttribute(cellStatus, "text", 1);
            treeView.AppendColumn(statusColumn);

            // Resource Usage column
            var resourceColumn = new TreeViewColumn { Title = "Resources" };
            var cellResource = new CellRendererText();
            resourceColumn.PackStart(cellResource, true);
            resourceColumn.AddAttribute(cellResource, "text", 2);
            treeView.AppendColumn(resourceColumn);

            var runAllButton = new Button("Run All Scripts");
            var stopAllButton = new Button("Stop All Processes");
            var scriptScroll = new ScrolledWindow();
            scriptScroll.SetSizeRequest(600, 400);
            scriptScroll.Add(treeView);

            // Add Run button column
            var runColumn = new TreeViewColumn { Title = "Run" };
            var cellRun = new CellRendererText 
            { 
                Text = "▶ Run",
                Foreground = "#4CAF50"
            };
            runColumn.PackStart(cellRun, true);
            treeView.AppendColumn(runColumn);

            // Add Delete button column  
            var deleteColumn = new TreeViewColumn { Title = "Delete" };
            var cellDelete = new CellRendererText 
            { 
                Text = "🗑 Delete",
                Foreground = "#F44336"
            };
            deleteColumn.PackStart(cellDelete, true);
            treeView.AppendColumn(deleteColumn);

            // Handle row clicks for Run and Delete
            treeView.ButtonPressEvent += (o, args) =>
            {
                if (isDisposing) return;
                
                if (args.Event.Button == 1) // Left click
                {
                    TreePath path;
                    TreeViewColumn column;
                    if (treeView.GetPathAtPos((int)args.Event.X, (int)args.Event.Y, out path, out column))
                    {
                        TreeIter iter;
                        if (scriptStore.GetIter(out iter, path))
                        {
                            var title = (string)scriptStore.GetValue(iter, 0);
                            var script = scripts.Find(s => s.Title == title);
                            
                            if (column == runColumn && script != null)
                            {
                                RunScript(script.Content, title);
                            }
                            else if (column == deleteColumn)
                            {
                                var confirmDialog = new MessageDialog(
                                    window,
                                    DialogFlags.Modal,
                                    MessageType.Question,
                                    ButtonsType.YesNo,
                                    $"Are you sure you want to delete the script '{title}'?");
                                confirmDialog.Title = "Confirm Delete";
                                
                                try
                                {
                                    if (confirmDialog.Run() == (int)ResponseType.Yes)
                                    {
                                        scripts.RemoveAll(s => s.Title == title);
                                        scriptStore.Remove(ref iter);
                                        SaveScripts();
                                    }
                                }
                                finally
                                {
                                    confirmDialog.Destroy();
                                }
                            }
                        }
                    }
                }
            };

            var buttonBox = new Box(Orientation.Horizontal, 10);
            buttonBox.PackStart(runAllButton, true, true, 0);
            buttonBox.PackStart(stopAllButton, true, true, 0);
            
            rightBox.PackStart(buttonBox, false, false, 0);
            rightBox.PackStart(scriptScroll, true, true, 0);

            mainBox.PackStart(leftBox, true, true, 5);
            mainBox.PackStart(rightBox, true, true, 5);

            window.Add(mainBox);

            // Event handlers
            addButton.Clicked += (s, e) =>
            {
                if (isDisposing) return;
                
                var title = titleEntry.Text.Trim();
                var scriptText = textView.Buffer.Text.Trim();
                if (string.IsNullOrEmpty(title))
                    title = $"Script {scripts.Count + 1}";
                if (!string.IsNullOrEmpty(scriptText))
                {
                    scripts.Add(new Script { Title = title, Content = scriptText });
                    scriptStore.AppendValues(title, "⚫ Stopped", "");
                    SaveScripts();
                    titleEntry.Text = "";
                    textView.Buffer.Text = "";
                }
            };

            fileButton.Clicked += (s, e) =>
            {
                if (isDisposing) return;
                
                var dialog = new FileChooserDialog(
                    "Choose a bash script file",
                    window,
                    FileChooserAction.Open,
                    "Cancel", ResponseType.Cancel,
                    "Open", ResponseType.Accept);

                var filter = new FileFilter();
                filter.AddPattern("*.sh");
                dialog.AddFilter(filter);

                try
                {
                    if (dialog.Run() == (int)ResponseType.Accept)
                    {
                        var filePath = dialog.Filename;
                        if (File.Exists(filePath))
                        {
                            var scriptText = File.ReadAllText(filePath);
                            var title = titleEntry.Text.Trim();
                            if (string.IsNullOrEmpty(title))
                                title = Path.GetFileNameWithoutExtension(filePath);
                            scripts.Add(new Script { Title = title, Content = scriptText });
                            scriptStore.AppendValues(title, "⚫ Stopped", "");
                            SaveScripts();
                            titleEntry.Text = "";
                        }
                    }
                }
                finally
                {
                    dialog.Destroy();
                }
            };

            runAllButton.Clicked += (s, e) =>
            {
                if (isDisposing) return;
                
                foreach (var script in scripts)
                {
                    RunScript(script.Content, script.Title);
                }
            };

            stopAllButton.Clicked += (s, e) =>
            {
                if (isDisposing) return;
                
                if (runningProcesses.Count == 0)
                {
                    SafeShowMessage("No processes are currently running.");
                    return;
                }

                var confirmDialog = new MessageDialog(
                    window,
                    DialogFlags.Modal,
                    MessageType.Question,
                    ButtonsType.YesNo,
                    $"Are you sure you want to stop all {runningProcesses.Count} running processes?");
                confirmDialog.Title = "Confirm Stop All";
                
                try
                {
                    if (confirmDialog.Run() == (int)ResponseType.Yes)
                    {
                        var processesToStop = new List<string>(runningProcesses.Keys);
                        foreach (var title in processesToStop)
                        {
                            if (runningProcesses.TryGetValue(title, out var process))
                            {
                                try
                                {
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                        Console.WriteLine($"Stopped process: {title}");
                                    }
                                    runningProcesses.Remove(title);
                                    if (processResources.ContainsKey(title))
                                    {
                                        processResources.Remove(title);
                                    }
                                    UpdateScriptStatus(title, "⚫ Stopped", "");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error stopping process '{title}': {ex.Message}");
                                }
                            }
                        }
                        SafeShowMessage($"Stopped all running processes.");
                    }
                }
                finally
                {
                    confirmDialog.Destroy();
                }
            };

            // Start resource monitoring timer
            resourceMonitorTimer = new System.Threading.Timer(UpdateResourceUsage, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

            // Properly handle window close event
            window.DeleteEvent += OnWindowDeleteEvent;
            window.ShowAll();
        }

        private void OnWindowDeleteEvent(object o, DeleteEventArgs e)
        {
            CleanupAndExit();
            e.RetVal = true; // Allow the window to close
        }

        private void CleanupAndExit()
        {
            isDisposing = true;
            
            // Stop resource monitoring
            resourceMonitorTimer?.Dispose();
            
            // Stop all running processes
            var processesToKill = new List<Process>(runningProcesses.Values);
            foreach (var process in processesToKill)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(1000); // Wait up to 1 second
                    }
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping process: {ex.Message}");
                }
            }
            runningProcesses.Clear();
            processResources.Clear();
            
            Application.Quit();
        }

        private void RunScript(string script, string title)
        {
            if (isDisposing) return;
            
            try
            {
                string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "temp");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, Path.GetRandomFileName() + ".sh");
                File.WriteAllText(tempFile, script);

                var chmodProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{tempFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                chmodProcess.Start();
                chmodProcess.WaitForExit();
                if (chmodProcess.ExitCode != 0)
                {
                    SafeShowMessage($"Error setting executable permissions for '{title}': {chmodProcess.StandardError.ReadToEnd()}");
                    File.Delete(tempFile);
                    chmodProcess.Dispose();
                    return;
                }
                chmodProcess.Dispose();

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = tempFile,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && !isDisposing)
                    {
                        // Log to console instead of showing popup for each line
                        Console.WriteLine($"[{title}] Output: {e.Data}");
                    }
                };
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && !isDisposing)
                    {
                        // Log to console instead of showing popup for each line
                        Console.WriteLine($"[{title}] Error: {e.Data}");
                    }
                };

                process.Exited += (s, e) =>
                {
                    GLib.Idle.Add(() =>
                    {
                        try
                        {
                            if (runningProcesses.ContainsKey(title))
                            {
                                runningProcesses.Remove(title);
                            }
                            if (processResources.ContainsKey(title))
                            {
                                processResources.Remove(title);
                            }
                            UpdateScriptStatus(title, "⚫ Stopped", "");
                            // Clean up temp file
                            if (File.Exists(tempFile))
                                File.Delete(tempFile);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during process cleanup: {ex.Message}");
                        }
                        return false;
                    });
                };

                process.EnableRaisingEvents = true;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                runningProcesses[title] = process;
                UpdateScriptStatus(title, "🟢 Running", "Starting...");
                Console.WriteLine($"Started process: {title} (PID: {process.Id})");
            }
            catch (Exception ex)
            {
                SafeShowMessage($"Error running script '{title}': {ex.Message}");
            }
        }

        private void ShowStopDialog()
        {
            if (isDisposing) return;
            
            if (runningProcesses.Count == 0)
            {
                SafeShowMessage("No processes are currently running.");
                return;
            }

            var dialog = new Dialog("Stop a Process", window, DialogFlags.Modal);
            var contentArea = dialog.ContentArea;
            var vbox = new Box(Orientation.Vertical, 5);

            var listStore = new ListStore(typeof(string));
            foreach (var title in runningProcesses.Keys)
            {
                listStore.AppendValues(title);
            }

            var treeView = new TreeView(listStore);
            treeView.AppendColumn("Running Processes", new CellRendererText(), "text", 0);
            var scrolledWindow = new ScrolledWindow();
            scrolledWindow.Add(treeView);
            vbox.PackStart(scrolledWindow, true, true, 0);

            var stopButton = new Button("Stop Selected Process");
            stopButton.Clicked += (s, e) =>
            {
                TreeIter iter;
                if (treeView.Selection.GetSelected(out iter))
                {
                    var title = (string)listStore.GetValue(iter, 0);
                    if (runningProcesses.TryGetValue(title, out var process))
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                if (title.Contains("Kafka"))
                                {
                                    var kafkaHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "kafka/kafka_2.13-3.9.1");
                                    var stopScript = Path.Combine(kafkaHome, "bin", "kafka-server-stop.sh");
                                    if (File.Exists(stopScript))
                                    {
                                        var stopProcess = new Process
                                        {
                                            StartInfo = new ProcessStartInfo
                                            {
                                                FileName = "/bin/bash",
                                                Arguments = stopScript,
                                                UseShellExecute = false,
                                                CreateNoWindow = true,
                                                RedirectStandardError = true
                                            }
                                        };
                                        stopProcess.Start();
                                        stopProcess.WaitForExit();
                                        if (stopProcess.ExitCode == 0)
                                        {
                                            SafeShowMessage($"Kafka process for '{title}' stopped successfully.");
                                        }
                                        else
                                        {
                                            SafeShowMessage($"Error stopping Kafka for '{title}': {stopProcess.StandardError.ReadToEnd()}");
                                        }
                                        stopProcess.Dispose();
                                    }
                                    else
                                    {
                                        SafeShowMessage($"Kafka stop script not found for '{title}'. Falling back to force kill.");
                                        process.Kill();
                                    }
                                }
                                else
                                {
                                    process.Kill();
                                    SafeShowMessage($"Process for '{title}' stopped.");
                                }
                            }
                            runningProcesses.Remove(title);
                            dialog.Respond(ResponseType.Cancel); // Close the dialog instead of keeping it open
                        }
                        catch (Exception ex)
                        {
                            SafeShowMessage($"Error stopping process '{title}': {ex.Message}");
                        }
                    }
                }
            };
            vbox.PackStart(stopButton, false, false, 0);

            contentArea.Add(vbox);
            dialog.AddButton("Cancel", ResponseType.Cancel);
            dialog.ShowAll();

            try
            {
                dialog.Run();
            }
            finally
            {
                dialog.Destroy();
            }
        }

        private void UpdateScriptStatus(string title, string status, string resources)
        {
            if (isDisposing) return;
            
            GLib.Idle.Add(() =>
            {
                if (isDisposing) return false;
                
                try
                {
                    TreeIter iter;
                    if (scriptStore.GetIterFirst(out iter))
                    {
                        do
                        {
                            var scriptTitle = (string)scriptStore.GetValue(iter, 0);
                            if (scriptTitle == title)
                            {
                                scriptStore.SetValue(iter, 1, status);
                                scriptStore.SetValue(iter, 2, resources);
                                break;
                            }
                        } while (scriptStore.IterNext(ref iter));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating script status: {ex.Message}");
                }
                return false;
            });
        }

        private void UpdateResourceUsage(object state)
        {
            if (isDisposing) return;
            
            var currentProcesses = new Dictionary<string, Process>(runningProcesses);
            
            foreach (var kvp in currentProcesses)
            {
                var title = kvp.Key;
                var process = kvp.Value;
                
                try
                {
                    if (!process.HasExited)
                    {
                        // Get process info using ps command
                        var psProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "ps",
                                Arguments = $"-p {process.Id} -o pid,pcpu,rss --no-headers",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        
                        psProcess.Start();
                        var output = psProcess.StandardOutput.ReadToEnd();
                        psProcess.WaitForExit();
                        psProcess.Dispose();
                        
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var parts = output.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                if (double.TryParse(parts[1], out double cpu) && long.TryParse(parts[2], out long memory))
                                {
                                    processResources[title] = new ProcessResourceInfo
                                    {
                                        CpuUsage = cpu,
                                        MemoryUsage = memory,
                                        LastUpdate = DateTime.Now
                                    };
                                    
                                    var memoryMB = memory / 1024.0;
                                    var resourceText = $"CPU: {cpu:F1}% | MEM: {memoryMB:F1}MB";
                                    UpdateScriptStatus(title, "🟢 Running", resourceText);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error monitoring process '{title}': {ex.Message}");
                }
            }
        }

        private void SafeShowMessage(string message)
        {
            if (isDisposing) return;
            
            try
            {
                var dialog = new MessageDialog(
                    window,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.Ok,
                    message);
                dialog.Run();
                dialog.Destroy();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing message dialog: {ex.Message}");
            }
        }

        private List<Script> LoadScripts()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var scriptsData = JsonSerializer.Deserialize<List<Script>>(json);
                    if (scriptsData == null)
                    {
                        Console.WriteLine("Warning: No valid scripts found in scripts.json. Starting with an empty list.");
                        return new List<Script>();
                    }
                    return scriptsData;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error loading scripts: Invalid JSON format - {ex.Message}. Starting with an empty list.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading scripts: {ex.Message}. Starting with an empty list.");
            }
            return new List<Script>();
        }

        private void SaveScripts()
        {
            if (isDisposing) return;
            
            try
            {
                var json = JsonSerializer.Serialize(scripts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving scripts: {ex.Message}");
            }
        }

        public void Run()
        {
            Application.Run();
        }
    }
}