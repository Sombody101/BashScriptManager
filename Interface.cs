using Gdk;
using Gtk;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;
using Window = Gtk.Window;

namespace BashScriptManager;

public class Interface
{
    private const string STATUS_STOPPED = "⚫ Stopped";

    private static readonly JsonSerializerOptions s_serializerOptions = new JsonSerializerOptions { WriteIndented = true };

    private readonly Window window;
    private readonly ListStore scriptStore = new(typeof(string), typeof(string), typeof(string)); // Title, Status, Resources
    private readonly List<Script> scripts = [];
    private readonly Dictionary<string, Process> runningProcesses = [];
    private readonly Dictionary<string, ProcessResourceInfo> processResources = [];
    private const string ConfigFile = "scripts.json";
    private bool isDisposing = false;
    private readonly System.Threading.Timer resourceMonitorTimer;

    public Interface()
    {
        Application.Init();

        window = new Window(string.Empty);
        InitUserInterface();

        resourceMonitorTimer = new System.Threading.Timer(UpdateResourceUsage, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        window.DeleteEvent += OnWindowDeleteEvent;
        window.ShowAll();
    }

    public void Run()
    {
        Application.Run();
    }

    private void InitUserInterface()
    {
        window.SetDefaultSize(1300, 800);

        Gdk.Geometry hints = new()
        {
            MinWidth = 900,
            MinHeight = 600
        };
        window.SetGeometryHints(window, hints, Gdk.WindowHints.MinSize);

        Box mainBox = new(Orientation.Horizontal, 10);
        Box leftBox = new(Orientation.Vertical, 10);
        Box rightBox = new(Orientation.Vertical, 10);

        HeaderBar headerBar = new()
        {
            Title = "Bash Script Manager",
            ShowCloseButton = true
        };

        string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Information_icon.svg-removebg-preview(2).png");
        Pixbuf original = new(imagePath);
        Pixbuf scaled = original.ScaleSimple(50, 50, InterpType.Bilinear);
        Image image = new(scaled);

        Button infoButton = new()
        {
            Relief = ReliefStyle.None
        };
        infoButton.Add(image);
        image.Show();
        infoButton.Show();

        infoButton.Clicked += (s, e) =>
        {
            MessageDialog dialog = new(
                window,
                DialogFlags.Modal,
                MessageType.Info,
                ButtonsType.Ok,
                "Developed by Sam O'Reilly @ https://github.com/Samoreilly");
            _ = dialog.Run();
            dialog.Destroy();
        };

        headerBar.PackStart(infoButton);

        headerBar.Add(infoButton);

        window.Titlebar = headerBar;

        Entry titleEntry = new() { PlaceholderText = "Enter script title" };
        TextView textView = new()
        {
            WrapMode = WrapMode.Word
        };

        ScrolledWindow scrolledWindow = new();
        scrolledWindow.SetSizeRequest(600, 400);
        scrolledWindow.Add(textView);

        Button addButton = new("Add Script");
        Button fileButton = new("Add from File");
        Box inputBox = new(Orientation.Horizontal, 5);
        inputBox.PackStart(addButton, false, false, 0);
        inputBox.PackStart(fileButton, false, false, 0);

        Button stopButton = new("Stop Process");
        stopButton.Clicked += (s, e) => ShowStopDialog();
        leftBox.PackStart(titleEntry, false, false, 0);
        leftBox.PackStart(scrolledWindow, true, true, 0);
        leftBox.PackStart(inputBox, false, false, 0);
        leftBox.PackStart(stopButton, false, false, 0);

        scripts.AddRange(LoadScripts().Select(s =>
        {
            _ = scriptStore.AppendValues(s.Title, STATUS_STOPPED, string.Empty);
            return s;
        }));

        TreeView treeView = new(scriptStore);

        TreeViewColumn titleColumn = new() { Title = "Script Title" };
        CellRendererText cellText = new();
        titleColumn.PackStart(cellText, true);
        titleColumn.AddAttribute(cellText, "text", 0);
        _ = treeView.AppendColumn(titleColumn);

        TreeViewColumn statusColumn = new() { Title = "Status" };
        CellRendererText cellStatus = new();
        statusColumn.PackStart(cellStatus, true);
        statusColumn.AddAttribute(cellStatus, "text", 1);
        _ = treeView.AppendColumn(statusColumn);

        TreeViewColumn resourceColumn = new() { Title = "Resources" };
        CellRendererText cellResource = new();
        resourceColumn.PackStart(cellResource, true);
        resourceColumn.AddAttribute(cellResource, "text", 2);
        _ = treeView.AppendColumn(resourceColumn);

        Button runAllButton = new("Run All Scripts");
        Button stopAllButton = new("Stop All Processes");
        ScrolledWindow scriptScroll = new();
        scriptScroll.SetSizeRequest(600, 400);
        scriptScroll.Add(treeView);

        TreeViewColumn runColumn = new() { Title = "Run" };
        CellRendererText cellRun = new()
        {
            Text = "▶ Run",
            Foreground = "#4CAF50"
        };
        runColumn.PackStart(cellRun, true);
        _ = treeView.AppendColumn(runColumn);

        TreeViewColumn deleteColumn = new() { Title = "Delete" };
        CellRendererText cellDelete = new()
        {
            Text = "🗑 Delete",
            Foreground = "#F44336"
        };
        deleteColumn.PackStart(cellDelete, true);
        _ = treeView.AppendColumn(deleteColumn);

        treeView.ButtonPressEvent += (o, args) =>
        {
            if (isDisposing)
            {
                return;
            }

            if (args.Event.Button == 1)
            {
                if (treeView.GetPathAtPos((int)args.Event.X, (int)args.Event.Y, out TreePath path, out TreeViewColumn column))
                {
                    if (scriptStore.GetIter(out TreeIter iter, path))
                    {
                        string title = (string)scriptStore.GetValue(iter, 0);
                        Script? script = scripts.Find(s => s.Title == title);

                        if (column == runColumn && script != null)
                        {
                            RunScript(script.Content, title);
                        }
                        else if (column == deleteColumn)
                        {
                            MessageDialog confirmDialog = new(
                                window,
                                DialogFlags.Modal,
                                MessageType.Question,
                                ButtonsType.YesNo,
                                $"Are you sure you want to delete the script '{title}'?")
                            {
                                Title = "Confirm Delete"
                            };

                            try
                            {
                                if (confirmDialog.Run() == (int)ResponseType.Yes)
                                {
                                    _ = scripts.RemoveAll(s => s.Title == title);
                                    _ = scriptStore.Remove(ref iter);
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

        Box buttonBox = new(Orientation.Horizontal, 10);
        buttonBox.PackStart(runAllButton, true, true, 0);
        buttonBox.PackStart(stopAllButton, true, true, 0);

        rightBox.PackStart(buttonBox, false, false, 0);
        rightBox.PackStart(scriptScroll, true, true, 0);

        mainBox.PackStart(leftBox, true, true, 5);
        mainBox.PackStart(rightBox, true, true, 5);

        window.Add(mainBox);

        addButton.Clicked += (s, e) =>
        {
            if (isDisposing)
            {
                return;
            }

            string title = titleEntry.Text.Trim();
            string scriptText = textView.Buffer.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = $"Script {scripts.Count + 1}";
            }

            if (!string.IsNullOrEmpty(scriptText))
            {
                scripts.Add(new Script { Title = title, Content = scriptText });
                _ = scriptStore.AppendValues(title, STATUS_STOPPED, string.Empty);
                SaveScripts();
                titleEntry.Text = string.Empty;
                textView.Buffer.Text = string.Empty;
            }
        };

        fileButton.Clicked += (s, e) =>
        {
            if (isDisposing)
            {
                return;
            }

            FileChooserDialog dialog = new(
                "Choose a bash script file",
                window,
                FileChooserAction.Open,
                "Cancel", ResponseType.Cancel,
                "Open", ResponseType.Accept);

            FileFilter filter = new();
            filter.AddPattern("*.sh");
            dialog.AddFilter(filter);

            try
            {
                if (dialog.Run() == (int)ResponseType.Accept)
                {
                    string filePath = dialog.Filename;
                    if (File.Exists(filePath))
                    {
                        string scriptText = File.ReadAllText(filePath);
                        string title = titleEntry.Text.Trim();
                        if (string.IsNullOrEmpty(title))
                        {
                            title = Path.GetFileNameWithoutExtension(filePath);
                        }

                        scripts.Add(new Script { Title = title, Content = scriptText });
                        _ = scriptStore.AppendValues(title, STATUS_STOPPED, string.Empty);
                        SaveScripts();
                        titleEntry.Text = string.Empty;
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
            if (isDisposing)
            {
                return;
            }

            foreach (Script script in scripts)
            {
                RunScript(script.Content, script.Title);
            }
        };

        stopAllButton.Clicked += (s, e) =>
        {
            if (isDisposing)
            {
                return;
            }

            if (runningProcesses.Count == 0)
            {
                SafeShowMessage("No processes are currently running.");
                return;
            }

            MessageDialog confirmDialog = new(
                window,
                DialogFlags.Modal,
                MessageType.Question,
                ButtonsType.YesNo,
                $"Are you sure you want to stop all {runningProcesses.Count} running processes?")
            {
                Title = "Confirm Stop All"
            };

            try
            {
                if (confirmDialog.Run() != (int)ResponseType.Yes)
                {
                    return;
                }

                List<string> processesToStop = [.. runningProcesses.Keys];
                foreach (string title in processesToStop)
                {
                    if (!runningProcesses.TryGetValue(title, out Process? process))
                    {
                        continue;
                    }
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            AnsiConsole.WriteLine($"Stopped process: {title}");
                        }

                        _ = runningProcesses.Remove(title);

                        if (processResources.ContainsKey(title))
                        {
                            _ = processResources.Remove(title);
                        }

                        UpdateScriptStatus(title, STATUS_STOPPED, string.Empty);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteLine($"Error stopping process '{title}': {ex.Message}");
                    }
                }

                SafeShowMessage($"Stopped all running processes.");
            }
            finally
            {
                confirmDialog.Destroy();
            }
        };
    }

    private void OnWindowDeleteEvent(object o, DeleteEventArgs e)
    {
        CleanupAndExit();
        e.RetVal = true;
    }

    private void CleanupAndExit()
    {
        isDisposing = true;

        resourceMonitorTimer?.Dispose();

        List<Process> processesToKill = [.. runningProcesses.Values];
        foreach (Process process in processesToKill)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    _ = process.WaitForExit(1000); // Wait up to 1 second
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Error stopping process: {ex.Message}");
            }
        }

        runningProcesses.Clear();
        processResources.Clear();

        Application.Quit();
    }

    private void RunScript(string script, string title)
    {
        if (isDisposing)
        {
            return;
        }

        try
        {
            string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "temp");
            if (!Directory.Exists(tempDir))
            {
                _ = Directory.CreateDirectory(tempDir);
            }

            string tempFile = Path.Combine(tempDir, Path.GetRandomFileName() + ".sh");
            File.WriteAllText(tempFile, script);

            using Process chmodProcess = new()
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

            _ = chmodProcess.Start();
            chmodProcess.WaitForExit();

            if (chmodProcess.ExitCode != 0)
            {
                SafeShowMessage($"Error setting executable permissions for '{title}': {chmodProcess.StandardError.ReadToEnd()}");
                File.Delete(tempFile);
                return;
            }

            using Process process = new()
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
                    AnsiConsole.WriteLine($"[[[blue]{title}[/]]] Output: {e.Data}");
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) && !isDisposing)
                {
                    AnsiConsole.WriteLine($"[[[red]{title}[/]]] Error: {e.Data}");
                }
            };

            process.Exited += (s, e) =>
            {
                _ = GLib.Idle.Add(() =>
                {
                    try
                    {
                        if (runningProcesses.ContainsKey(title))
                        {
                            _ = runningProcesses.Remove(title);
                        }
                        if (processResources.ContainsKey(title))
                        {
                            _ = processResources.Remove(title);
                        }
                        UpdateScriptStatus(title, STATUS_STOPPED, string.Empty);

                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsoleLogger.LogError($"Error during process cleanup: {ex.Message}");
                    }
                    return false;
                });
            };

            process.EnableRaisingEvents = true;
            _ = process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            runningProcesses[title] = process;
            UpdateScriptStatus(title, "🟢 Running", "Starting...");
            AnsiConsoleLogger.LogInformation($"Started process: {title} (PID: {process.Id})");
        }
        catch (Exception ex)
        {
            SafeShowMessage($"Error running script '{title}': {ex.Message}");
        }
    }

    private void ShowStopDialog()
    {
        if (isDisposing)
        {
            return;
        }

        if (runningProcesses.Count == 0)
        {
            SafeShowMessage("No processes are currently running.");
            return;
        }

        Dialog dialog = new("Stop a Process", window, DialogFlags.Modal);
        Box contentArea = dialog.ContentArea;
        Box vbox = new(Orientation.Vertical, 5);

        ListStore listStore = new(typeof(string));
        foreach (string title in runningProcesses.Keys)
        {
            _ = listStore.AppendValues(title);
        }

        TreeView treeView = new(listStore);
        _ = treeView.AppendColumn("Running Processes", new CellRendererText(), "text", 0);
        ScrolledWindow scrolledWindow = new()
        {
            treeView
        };
        vbox.PackStart(scrolledWindow, true, true, 0);

        Button stopButton = new("Stop Selected Process");
        stopButton.Clicked += (s, e) =>
        {
            if (!treeView.Selection.GetSelected(out TreeIter iter))
            {
                return;
            }

            string title = (string)listStore.GetValue(iter, 0);
            if (!runningProcesses.TryGetValue(title, out Process? process))
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    _ = runningProcesses.Remove(title);
                    dialog.Respond(ResponseType.Cancel);
                    return;
                }

                if (title.Contains("Kafka"))
                {
                    process.Kill();
                    SafeShowMessage($"Process for '{title}' stopped.");
                    return;
                }

                string kafkaHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "kafka/kafka_2.13-3.9.1");
                string stopScript = Path.Combine(kafkaHome, "bin", "kafka-server-stop.sh");
                if (File.Exists(stopScript))
                {
                    using Process stopProcess = new()
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

                    _ = stopProcess.Start();
                    stopProcess.WaitForExit();

                    if (stopProcess.ExitCode == 0)
                    {
                        SafeShowMessage($"Kafka process for '{title}' stopped successfully.");
                    }
                    else
                    {
                        SafeShowMessage($"Error stopping Kafka for '{title}': {stopProcess.StandardError.ReadToEnd()}");
                    }
                }
                else
                {
                    SafeShowMessage($"Kafka stop script not found for '{title}'. Falling back to force kill.");
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                SafeShowMessage($"Error stopping process '{title}': {ex.Message}");
            }
        };

        vbox.PackStart(stopButton, false, false, 0);

        contentArea.Add(vbox);
        _ = dialog.AddButton("Cancel", ResponseType.Cancel);
        dialog.ShowAll();

        try
        {
            _ = dialog.Run();
        }
        finally
        {
            dialog.Destroy();
        }
    }

    private void UpdateScriptStatus(string title, string status, string resources)
    {
        if (isDisposing)
        {
            return;
        }

        _ = GLib.Idle.Add(() =>
        {
            if (isDisposing)
            {
                return false;
            }

            try
            {
                if (scriptStore.GetIterFirst(out TreeIter iter))
                {
                    do
                    {
                        string scriptTitle = (string)scriptStore.GetValue(iter, 0);
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
                AnsiConsoleLogger.LogError($"Error updating script status: {ex.Message}");
            }
            return false;
        });
    }

    private void UpdateResourceUsage(object? state)
    {
        if (isDisposing)
        {
            return;
        }

        Dictionary<string, Process> currentProcesses = new(runningProcesses);

        foreach (KeyValuePair<string, Process> kvp in currentProcesses)
        {
            string title = kvp.Key;
            Process process = kvp.Value;

            try
            {
                if (!process.HasExited)
                {
                    using Process psProcess = new()
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

                    _ = psProcess.Start();
                    string output = psProcess.StandardOutput.ReadToEnd();
                    psProcess.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        string[] parts = output.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3 && double.TryParse(parts[1], out double cpu) && long.TryParse(parts[2], out long memory))
                        {
                            processResources[title] = new ProcessResourceInfo
                            {
                                CpuUsage = cpu,
                                MemoryUsage = memory,
                                LastUpdate = DateTime.Now
                            };

                            double memoryMB = memory / 1024.0;
                            string resourceText = $"CPU: {cpu:F1}% | MEM: {memoryMB:F1}MB";
                            UpdateScriptStatus(title, "🟢 Running", resourceText);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsoleLogger.LogError($"Error monitoring process '{title}': {ex.Message}");
            }
        }
    }

    private void SafeShowMessage(string message)
    {
        if (isDisposing)
        {
            return;
        }

        try
        {
            MessageDialog dialog = new(
                window,
                DialogFlags.Modal,
                MessageType.Info,
                ButtonsType.Ok,
                message);
            _ = dialog.Run();
            dialog.Destroy();
        }
        catch (Exception ex)
        {
            AnsiConsoleLogger.LogError($"Error showing message dialog: {ex.Message}");
        }
    }

    private void SaveScripts()
    {
        if (isDisposing)
        {
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(scripts, s_serializerOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            AnsiConsoleLogger.LogError($"Error saving scripts: {ex.Message}");
        }
    }

    private static List<Script> LoadScripts()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                List<Script>? scriptsData = JsonSerializer.Deserialize<List<Script>>(json);

                if (scriptsData is null)
                {
                    AnsiConsoleLogger.LogError("Warning: No valid scripts found in scripts.json. Starting with an empty list.");
                    return [];
                }

                return scriptsData;
            }
        }
        catch (JsonException ex)
        {
            AnsiConsoleLogger.LogError($"Error loading scripts: Invalid JSON format - {ex.Message}. Starting with an empty list.");
        }
        catch (Exception ex)
        {
            AnsiConsoleLogger.LogError($"Error loading scripts: {ex.Message}. Starting with an empty list.");
        }

        return [];
    }
}