using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LaunchForge.Core;

namespace LaunchForge.Desktop;

public sealed class MainWindow : Window
{
    private Workflow _workflow = Example();
    private readonly StackPanel _editor = new() { Spacing = 16, Margin = new Thickness(28) };
    private readonly TextBox _status = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, Margin = new Thickness(28, 0, 28, 16), Text = "Ready. Configure your project folder and commands, then export a launcher." };
    private readonly Button _cancel = new() { Content = "Cancel running operation", IsVisible = false, Margin = new Thickness(28, 0, 28, 12), HorizontalAlignment = HorizontalAlignment.Left };
    private CancellationTokenSource? _operation;
    private ExportFormat _format = OperatingSystem.IsWindows() ? ExportFormat.WindowsBatch : ExportFormat.LinuxShell;

    public MainWindow()
    {
        Title = "LaunchForge — Workflow launcher builder";
        Width = 980; Height = 820; MinWidth = 700; MinHeight = 600;
        var root = new DockPanel();
        var bottom = new StackPanel();
        bottom.Children.Add(_cancel); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = _editor });
        Content = root;
        _cancel.Click += (_, _) => _operation?.Cancel();
        Closing += (_, args) =>
        {
            // Keep the window alive while an export cleans up or a running process is stopped.
            if (_operation is not null) { args.Cancel = true; _operation.Cancel(); Log("Cancelling. Close the window again when the operation finishes."); }
        };
        Render();
    }

    private static Workflow Example() => new()
    {
        Name = "Open and run my project",
        WorkingDirectory = Environment.CurrentDirectory,
        Steps =
        [
            new() { Name = "Open project in VS Code", WindowsCommand = "code .", LinuxCommand = "code .", WaitForExit = true },
            new() { Name = "Run project", WindowsCommand = "dotnet run", LinuxCommand = "dotnet run", WaitForExit = true }
        ]
    };

    private void Render()
    {
        _editor.Children.Clear();
        _editor.Children.Add(new TextBlock { Text = "LaunchForge", FontSize = 32, FontWeight = FontWeight.Bold });
        _editor.Children.Add(new TextBlock { Text = "Build a one-click launcher from a sequence of actions.", FontSize = 16 });
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(ActionButton("New workflow", () => { _workflow = new Workflow(); Render(); }));
        toolbar.Children.Add(ActionButton("VS Code example", () => { _workflow = Example(); Render(); }));
        toolbar.Children.Add(AsyncButton("Open workflow…", OpenAsync));
        toolbar.Children.Add(AsyncButton("Save workflow…", SaveAsync));
        _editor.Children.Add(toolbar);
        _editor.Children.Add(Field("Launcher name", _workflow.Name, value => _workflow.Name = value));
        _editor.Children.Add(Field("Default working folder (use a full path for the target computer)", _workflow.WorkingDirectory, value => _workflow.WorkingDirectory = value));
        _editor.Children.Add(AsyncButton("Choose working folder…", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) { _workflow.WorkingDirectory = path; Render(); }
        }));
        _editor.Children.Add(new TextBlock { Text = "Actions", FontSize = 22, FontWeight = FontWeight.SemiBold });
        for (int index = 0; index < _workflow.Steps.Count; index++)
        {
            int position = index;
            var step = _workflow.Steps[index];
            var fields = new StackPanel { Spacing = 10 };
            var heading = new WrapPanel();
            heading.Children.Add(new TextBlock { Text = $"Action {index + 1}", FontSize = 18, Margin = new Thickness(0, 6, 18, 0) });
            if (index > 0) heading.Children.Add(ActionButton("Move up", () => { (_workflow.Steps[position - 1], _workflow.Steps[position]) = (_workflow.Steps[position], _workflow.Steps[position - 1]); Render(); }));
            if (index < _workflow.Steps.Count - 1) heading.Children.Add(ActionButton("Move down", () => { (_workflow.Steps[position + 1], _workflow.Steps[position]) = (_workflow.Steps[position], _workflow.Steps[position + 1]); Render(); }));
            heading.Children.Add(ActionButton("Remove", () => { _workflow.Steps.RemoveAt(position); Render(); }));
            fields.Children.Add(heading);
            fields.Children.Add(Field("Action name", step.Name, value => step.Name = value));
            fields.Children.Add(Field("Windows command (cmd.exe)", step.WindowsCommand, value => step.WindowsCommand = value));
            fields.Children.Add(Field("Linux command (bash)", step.LinuxCommand, value => step.LinuxCommand = value));
            fields.Children.Add(Field("Working folder override (optional)", step.WorkingDirectory, value => step.WorkingDirectory = value));
            var wait = new CheckBox { Content = "Wait for completion before the next action; stop if it fails", IsChecked = step.WaitForExit };
            wait.IsCheckedChanged += (_, _) => step.WaitForExit = wait.IsChecked == true;
            fields.Children.Add(wait);
            _editor.Children.Add(new Border { Child = fields, Padding = new Thickness(18), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, CornerRadius = new CornerRadius(8) });
        }
        _editor.Children.Add(ActionButton("+ Add action", () => { _workflow.Steps.Add(new WorkflowStep()); Render(); }));
        _editor.Children.Add(new TextBlock { Text = "Export", FontSize = 22, FontWeight = FontWeight.SemiBold });
        var formats = Enum.GetValues<ExportFormat>();
        var choice = new ComboBox { ItemsSource = new[] { "Windows batch (.bat)", "Linux shell (.sh)", "Windows executable (.exe, x64)", "Linux executable (x64)" }, SelectedIndex = Array.IndexOf(formats, _format), MinWidth = 320 };
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) _format = formats[choice.SelectedIndex]; };
        _editor.Children.Add(choice);
        _editor.Children.Add(new TextBlock { Text = "EXE / Linux binary export needs the .NET 10 SDK and internet for the first runtime download.\nCommands and project files must exist on the computer running your launcher.\nCommands run with your permissions. Unchecked actions keep running independently.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var footer = new WrapPanel();
        footer.Children.Add(AsyncButton("Export launcher…", ExportAsync));
        footer.Children.Add(AsyncButton("Run workflow on this computer", () => WithOperationAsync(token => WorkflowRunner.RunAsync(_workflow, Log, token))));
        _editor.Children.Add(footer);
    }

    private static Control Field(string label, string value, Action<string> update)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        var input = new TextBox { Text = value };
        input.TextChanged += (_, _) => update(input.Text ?? "");
        panel.Children.Add(input);
        return panel;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (_, _) => action();
        return button;
    }

    private Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Log(error.Message); } };
        return button;
    }

    private void Log(string message) => Dispatcher.UIThread.Post(() => _status.Text = message);

    private async Task WithOperationAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        _editor.IsEnabled = false; _cancel.IsVisible = true;
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { Log("Cancelled. Actions already launched without waiting may still be running."); }
        finally { _operation.Dispose(); _operation = null; _editor.IsEnabled = true; _cancel.IsVisible = false; }
    }

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open workflow", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("LaunchForge workflow") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var loaded = Workflow.FromJson(await reader.ReadToEndAsync());
        if (loaded.Steps is null || loaded.Steps.Any(step => step is null)) throw new InvalidDataException("Invalid actions in workflow file.");
        _workflow = loaded; Render(); Log("Workflow opened. Review the commands before running it.");
    }

    private async Task SaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save workflow", SuggestedFileName = SafeName() + ".json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_workflow.ToJson()); Log("Workflow saved.");
    }

    private string SafeName() => string.Concat(_workflow.Name.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')).Trim('-') is { Length: > 0 } name ? name : "launcher";

    private async Task ExportAsync()
    {
        _workflow.Validate(_format is ExportFormat.WindowsBatch or ExportFormat.WindowsExecutable);
        var extension = _format switch { ExportFormat.WindowsBatch => ".bat", ExportFormat.LinuxShell => ".sh", ExportFormat.WindowsExecutable => ".exe", _ => "" };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export launcher", SuggestedFileName = SafeName() + extension, DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException("Choose a local destination for the launcher.");
        await WithOperationAsync(async token => { await LauncherExporter.ExportAsync(_workflow, _format, path, Log, token); Log($"Launcher exported: {path}"); });
    }
}
