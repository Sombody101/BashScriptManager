using Gtk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ConsoleApp2
{
    public class Script
    {
        public string Title { get; set; }
        public string Content { get; set; }
    }

    public class Interface
    {
        private Window window;
        private ListStore scriptStore;
        private List<Script> scripts;
        private const string ConfigFile = "scripts.json";

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
            
            var cssProvider = new CssProvider();
            cssProvider.LoadFromData(
                "window { background-color: #e0e0e0; }" +
                "button { background-color: #6c8296; color: #f5f5f5; }" +
                "treeview { background-color: #d3d7dc; }" +
                "textview { background-color: #f5f5f5; color: #333333; }" +
                "entry { background-color: #f5f5f5; color: #333333; }"
            );
            StyleContext.AddProviderForScreen(Gdk.Screen.Default, cssProvider, 800);
            
            var mainBox = new Box(Orientation.Horizontal, 10);
            var leftBox = new Box(Orientation.Vertical, 10);
            var rightBox = new Box(Orientation.Vertical, 10);
            
            var titleEntry = new Entry
            {
                PlaceholderText = "Enter script title"
            };
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

            leftBox.PackStart(titleEntry, false, false, 0);
            leftBox.PackStart(scrolledWindow, true, true, 0);
            leftBox.PackStart(inputBox, false, false, 0);
            
            scripts = LoadScripts();
            scriptStore = new ListStore(typeof(string));
            foreach (var script in scripts)
            {
                scriptStore.AppendValues(script.Title);
            }

            var treeView = new TreeView(scriptStore);
            var titleColumn = new TreeViewColumn
            {
                Title = "Script Title"
            };
            var cellText = new CellRendererText();
            titleColumn.PackStart(cellText, true);
            titleColumn.AddAttribute(cellText, "text", 0);
            treeView.AppendColumn(titleColumn);

            var runAllButton = new Button("Run All Scripts");
            var scriptScroll = new ScrolledWindow();
            scriptScroll.SetSizeRequest(600, 400);
            scriptScroll.Add(treeView);
            
            var runColumn = new TreeViewColumn { Title = "Run" };
            var cellRun = new CellRendererToggle();
            cellRun.Activatable = true;
            runColumn.PackStart(cellRun, true);
            cellRun.Toggled += (s, e) =>
            {
                TreeIter iter;
                if (scriptStore.GetIter(out iter, new TreePath(e.Path)))
                {
                    var title = (string)scriptStore.GetValue(iter, 0);
                    var script = scripts.Find(s => s.Title == title);
                    if (script != null)
                        RunScript(script.Content);
                }
            };
            treeView.AppendColumn(runColumn);
            
            var deleteColumn = new TreeViewColumn { Title = "Delete" };
            var cellDelete = new CellRendererToggle();
            cellDelete.Activatable = true;
            deleteColumn.PackStart(cellDelete, true);
            cellDelete.Toggled += (s, e) =>
            {
                TreeIter iter;
                if (scriptStore.GetIter(out iter, new TreePath(e.Path)))
                {
                    var title = (string)scriptStore.GetValue(iter, 0);
                    var confirmDialog = new MessageDialog(
                        window,
                        DialogFlags.Modal,
                        MessageType.Question,
                        ButtonsType.YesNo,
                        $"Are you sure you want to delete the script '{title}'?");
                    confirmDialog.Title = "Confirm Delete";
                    if (confirmDialog.Run() == (int)ResponseType.Yes)
                    {
                        scripts.RemoveAll(s => s.Title == title);
                        scriptStore.Remove(ref iter);
                        SaveScripts();
                    }
                    confirmDialog.Destroy();
                }
            };
            treeView.AppendColumn(deleteColumn);

            rightBox.PackStart(runAllButton, false, false, 0);
            rightBox.PackStart(scriptScroll, true, true, 0);

            mainBox.PackStart(leftBox, true, true, 5);
            mainBox.PackStart(rightBox, true, true, 5);

            window.Add(mainBox);
            
            addButton.Clicked += (s, e) =>
            {
                var title = titleEntry.Text.Trim();
                var scriptText = textView.Buffer.Text.Trim();
                if (string.IsNullOrEmpty(title))
                    title = $"Script {scripts.Count + 1}";
                if (!string.IsNullOrEmpty(scriptText))
                {
                    scripts.Add(new Script { Title = title, Content = scriptText });
                    scriptStore.AppendValues(title);
                    SaveScripts();
                    titleEntry.Text = "";
                    textView.Buffer.Text = "";
                }
            };

            fileButton.Clicked += (s, e) =>
            {
                var dialog = new FileChooserDialog(
                    "Choose a bash script file",
                    window,
                    FileChooserAction.Open,
                    "Cancel", ResponseType.Cancel,
                    "Open", ResponseType.Accept);

                var filter = new FileFilter();
                filter.AddPattern("*.sh");
                dialog.AddFilter(filter);

                if (dialog.Run() == (int)ResponseType.Accept)
                {
                    var filePath = dialog.Filename;
                    var scriptText = File.ReadAllText(filePath);
                    var title = titleEntry.Text.Trim();
                    if (string.IsNullOrEmpty(title))
                        title = Path.GetFileNameWithoutExtension(filePath);
                    scripts.Add(new Script { Title = title, Content = scriptText });
                    scriptStore.AppendValues(title);
                    SaveScripts();
                    titleEntry.Text = "";
                }
                dialog.Destroy();
            };

            runAllButton.Clicked += (s, e) =>
            {
                foreach (var script in scripts)
                {
                    RunScript(script.Content);
                }
            };

            window.ShowAll();
        }

        private void RunScript(string script)
{
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
            ShowMessage($"Error setting executable permissions: {chmodProcess.StandardError.ReadToEnd()}");
            File.Delete(tempFile);
            return;
        }

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

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrEmpty(output))
            ShowMessage($"Output:\n{output}");
        if (!string.IsNullOrEmpty(error))
            ShowMessage($"Error:\n{error}");

        File.Delete(tempFile);
    }
    catch (Exception ex)
    {
        ShowMessage($"Error running script: {ex.Message}");
    }
}

        private void ShowMessage(string message)
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

        private List<Script> LoadScripts()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    return JsonSerializer.Deserialize<List<Script>>(json) ?? new List<Script>();
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"Error loading scripts: {ex.Message}");
            }
            return new List<Script>();
        }

        private void SaveScripts()
        {
            try
            {
                var json = JsonSerializer.Serialize(scripts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error saving scripts: {ex.Message}");
            }
        }

        public void Run()
        {
            window.DeleteEvent += (o, e) => { Application.Quit(); };
            window.Show();
            Application.Run();
        }
    }
    
}