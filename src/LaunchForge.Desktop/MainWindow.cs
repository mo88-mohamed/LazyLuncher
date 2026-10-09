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
    private static readonly IBrush Surface = Brush.Parse("#111C2E");
    private static readonly IBrush Outline = Brush.Parse("#29374E");
    private static readonly IBrush Muted = Brush.Parse("#A6B4CA");
    private static readonly IBrush Accent = Brush.Parse("#5EEAD4");
    private readonly Grid _editor = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(18, 16, 18, 0) };
    private readonly StackPanel _details = new() { Spacing = 12 };
    private ScrollViewer? _detailScroll;
    private readonly List<TextBlock> _actionLabels = [];
    private readonly TextBox _status = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 36, MaxHeight = 72, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 12, Foreground = Muted, Text = "Ready. Choose a project folder and configure your actions." };
    private readonly Button _cancel = new() { Content = "Cancel", IsVisible = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _operation;
    private ExportFormat _format = OperatingSystem.IsWindows() ? ExportFormat.WindowsBatch : ExportFormat.LinuxShell;
    private int _selectedAction;
    private int _commandTab = OperatingSystem.IsWindows() ? 0 : 1;

    public MainWindow()
    {
        Title = "LaunchForge";
        Width = 820; Height = 640; MinWidth = 640; MinHeight = 480;
        Background = Brush.Parse("#0B1220");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var statusRow = new DockPanel { Margin = new Thickness(18, 4, 18, 8) };
        DockPanel.SetDock(_cancel, Dock.Right);
        statusRow.Children.Add(_cancel); statusRow.Children.Add(_status);
        var bottom = new Border { Child = statusRow, BorderBrush = Outline, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(_editor);
        Content = root;
        Opened += (_, _) => FitToScreen();
        _cancel.Click += (_, _) => _operation?.Cancel();
        Closing += (_, args) =>
        {
            // Keep the window alive while an export cleans up or a running process is stopped.
            if (_operation is not null) { args.Cancel = true; _operation.Cancel(); Log("Cancelling. Close the window again when the operation finishes."); }
        };
        Render();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var availableWidth = Math.Max(1, area.Width / screen.Scaling - 32);
        var availableHeight = Math.Max(1, area.Height / screen.Scaling - 48);
        MinWidth = Math.Min(640, availableWidth);
        MinHeight = Math.Min(480, availableHeight);
        Width = Math.Min(820, availableWidth);
        Height = Math.Min(640, availableHeight);
        Position = new PixelPoint(area.X + (area.Width - (int)(Width * screen.Scaling)) / 2,
            area.Y + (area.Height - (int)(Height * screen.Scaling)) / 2);
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
        // The editor is reused when the workflow or action order changes.
        if (_detailScroll is not null) _detailScroll.Content = null;
        _editor.Children.Clear();
        var workflow = _workflow;
        _selectedAction = Math.Clamp(_selectedAction, 0, Math.Max(0, _workflow.Steps.Count - 1));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        brand.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Brush.Parse("#153D40"),
            Child = new TextBlock { Text = "LF", Foreground = Accent, FontWeight = FontWeight.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var title = new StackPanel { Spacing = 1 };
        title.Children.Add(new TextBlock { Text = "LaunchForge", FontSize = 21, FontWeight = FontWeight.Bold });
        title.Children.Add(Hint("Your tasks. One click."));
        brand.Children.Add(title);
        header.Children.Add(brand);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(ActionButton("New", () => { _workflow = new Workflow(); _selectedAction = 0; Render(); }));
        toolbar.Children.Add(AsyncButton("Open", OpenAsync));
        toolbar.Children.Add(AsyncButton("Save", SaveAsync));
        Grid.SetColumn(toolbar, 1); header.Children.Add(toolbar);
        _editor.Children.Add(header);

        var setup = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), Margin = new Thickness(0, 0, 0, 14) };
        var name = Field("Launcher name", workflow.Name, value => workflow.Name = value);
        name.Margin = new Thickness(0, 0, 12, 0); setup.Children.Add(name);
        var folderGroup = new StackPanel { Spacing = 5 };
        folderGroup.Children.Add(Label("Project folder"));
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var folderInput = Input(workflow.WorkingDirectory, value => workflow.WorkingDirectory = value);
        ToolTip.SetTip(folderInput, "Full folder path on the computer that will run the launcher.");
        folderRow.Children.Add(folderInput);
        var browse = AsyncButton("Browse", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                workflow.WorkingDirectory = path;
                folderInput.Text = path;
            }
        });
        browse.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);
        folderGroup.Children.Add(folderRow); Grid.SetColumn(folderGroup, 1); setup.Children.Add(folderGroup);
        Grid.SetRow(setup, 1); _editor.Children.Add(setup);

        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), Margin = new Thickness(0, 0, 0, 12) };
        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var sidebarHeading = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        sidebarHeading.Children.Add(new TextBlock { Text = $"Actions ({_workflow.Steps.Count})", FontSize = 14, FontWeight = FontWeight.SemiBold });
        sidebarHeading.Children.Add(Hint("Run from top to bottom"));
        DockPanel.SetDock(sidebarHeading, Dock.Top); sidebar.Children.Add(sidebarHeading);
        var sidebarFooter = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var add = ActionButton("+ Add action", AddAction); add.HorizontalAlignment = HorizontalAlignment.Stretch;
        sidebarFooter.Children.Add(add);
        var example = ActionButton("Use VS Code example", () => { _workflow = Example(); _selectedAction = 0; Render(); });
        example.HorizontalAlignment = HorizontalAlignment.Stretch; example.FontSize = 11;
        sidebarFooter.Children.Add(example);
        DockPanel.SetDock(sidebarFooter, Dock.Bottom); sidebar.Children.Add(sidebarFooter);
        _actionLabels.Clear();
        var items = new List<ListBoxItem>();
        for (var index = 0; index < _workflow.Steps.Count; index++)
        {
            var text = new TextBlock { Text = ActionTitle(index, workflow.Steps[index]), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _actionLabels.Add(text);
            var item = new ListBoxItem { Content = text, Padding = new Thickness(10, 12) };
            ToolTip.SetTip(item, workflow.Steps[index].Name);
            items.Add(item);
        }
        var actions = new ListBox { ItemsSource = items, SelectedIndex = _workflow.Steps.Count > 0 ? _selectedAction : -1, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        actions.SelectionChanged += (_, _) =>
        {
            if (actions.SelectedIndex < 0) return;
            _selectedAction = actions.SelectedIndex;
            RenderDetails();
        };
        sidebar.Children.Add(new Border { Child = actions, Background = Surface, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), ClipToBounds = true });
        workspace.Children.Add(sidebar);
        var detailCard = new Border
        {
            Child = _detailScroll = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            Background = Surface, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16)
        };
        Grid.SetColumn(detailCard, 1); workspace.Children.Add(detailCard);
        Grid.SetRow(workspace, 2); _editor.Children.Add(workspace);
        RenderDetails();

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var formats = Enum.GetValues<ExportFormat>();
        var choice = new ComboBox { ItemsSource = new[] { "Windows batch (.bat)", "Linux script (.sh)", "Windows EXE (x64)", "Linux binary (x64)" }, SelectedIndex = Array.IndexOf(formats, _format), HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 290, FontSize = 12 };
        choice.HorizontalAlignment = HorizontalAlignment.Left;
        choice.MinWidth = 200;
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) _format = formats[choice.SelectedIndex]; };
        ToolTip.SetTip(choice, "EXE and binary exports bundle the runtime and need the .NET SDK to build. Commands and project files must exist on the target computer.");
        footer.Children.Add(choice);
        var run = AsyncButton("Run now", () => WithOperationAsync(token => WorkflowRunner.RunAsync(_workflow, Log, token)));
        run.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(run, 1); footer.Children.Add(run);
        var export = AsyncButton("Export launcher", ExportAsync);
        export.Background = Accent; export.Foreground = Brush.Parse("#082F2C"); export.FontWeight = FontWeight.SemiBold;
        Grid.SetColumn(export, 2); footer.Children.Add(export);
        Grid.SetRow(footer, 3);
        _editor.Children.Add(footer);
    }

    private static string ActionTitle(int index, WorkflowStep step) => $"{index + 1:00}   {(string.IsNullOrWhiteSpace(step.Name) ? "Untitled action" : step.Name)}";

    private void AddAction()
    {
        _workflow.Steps.Add(new WorkflowStep { Name = $"Action {_workflow.Steps.Count + 1}" });
        _selectedAction = _workflow.Steps.Count - 1;
        Render();
    }

    private void MoveAction(int direction)
    {
        var destination = _selectedAction + direction;
        if (destination < 0 || destination >= _workflow.Steps.Count) return;
        (_workflow.Steps[_selectedAction], _workflow.Steps[destination]) = (_workflow.Steps[destination], _workflow.Steps[_selectedAction]);
        _selectedAction = destination;
        Render();
    }

    private void RenderDetails()
    {
        _details.Children.Clear();
        if (_workflow.Steps.Count == 0)
        {
            _details.Children.Add(new TextBlock { Text = "What should your launcher do?", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
            _details.Children.Add(Hint("Add your first action, or start with the VS Code example."));
            _details.Children.Add(ActionButton("+ Add first action", AddAction));
            return;
        }
        var position = _selectedAction;
        var step = _workflow.Steps[position];
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(new TextBlock { Text = $"Edit action {position + 1}", FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var up = ActionButton("\u2191", () => MoveAction(-1)); up.IsEnabled = position > 0; ToolTip.SetTip(up, "Move action up");
        var down = ActionButton("\u2193", () => MoveAction(1)); down.IsEnabled = position < _workflow.Steps.Count - 1; ToolTip.SetTip(down, "Move action down");
        controls.Children.Add(up); controls.Children.Add(down);
        var remove = ActionButton("Remove", () => { _workflow.Steps.RemoveAt(position); Render(); });
        remove.Foreground = Brush.Parse("#FCA5A5"); controls.Children.Add(remove);
        Grid.SetColumn(controls, 1); heading.Children.Add(controls);
        _details.Children.Add(heading);
        var actionLabel = _actionLabels[position];
        _details.Children.Add(Field("Action name", step.Name, value => { step.Name = value; actionLabel.Text = ActionTitle(position, step); }));
        var windows = Field("Command", step.WindowsCommand, value => step.WindowsCommand = value, "e.g. code . or dotnet run", command: true);
        var linux = Field("Command", step.LinuxCommand, value => step.LinuxCommand = value, "e.g. code . or npm run dev", command: true);
        windows.Margin = linux.Margin = new Thickness(0, 10, 0, 0);
        var commands = new TabControl
        {
            ItemsSource = new[] { new TabItem { Header = "Windows", Content = windows, FontSize = 13 }, new TabItem { Header = "Linux", Content = linux, FontSize = 13 } },
            SelectedIndex = _commandTab
        };
        commands.SelectionChanged += (_, _) => { if (commands.SelectedIndex >= 0) _commandTab = commands.SelectedIndex; };
        _details.Children.Add(commands);
        _details.Children.Add(Hint("Use the tab for the platform you want to export."));
        var wait = new CheckBox { Content = "Wait before starting the next action", IsChecked = step.WaitForExit, FontSize = 12 };
        wait.IsCheckedChanged += (_, _) => step.WaitForExit = wait.IsChecked == true;
        ToolTip.SetTip(wait, "When enabled, stop the workflow if this action fails. When disabled, the action keeps running independently.");
        _details.Children.Add(wait);
        var overrideField = Field("Working folder", step.WorkingDirectory, value => step.WorkingDirectory = value, "Leave blank to use the project folder");
        overrideField.Margin = new Thickness(0, 8, 0, 0);
        _details.Children.Add(new Expander { Header = "Use a different folder", Content = overrideField, IsExpanded = !string.IsNullOrWhiteSpace(step.WorkingDirectory), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Background = Surface, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) });
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Muted };
    private static TextBlock Hint(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    private static TextBox Input(string value, Action<string> update, string? placeholder = null, bool command = false)
    {
        var input = new TextBox { Text = value, Watermark = placeholder, FontSize = 13, Padding = new Thickness(10, 7), Background = Brush.Parse("#0C1525"), BorderBrush = Outline };
        if (command) input.FontFamily = new FontFamily("Cascadia Code, Consolas, DejaVu Sans Mono, monospace");
        var previous = value ?? "";
        input.TextChanged += (_, _) =>
        {
            var current = input.Text ?? "";
            if (current == previous) return;
            previous = current;
            update(current);
        };
        return input;
    }

    private static Control Field(string label, string value, Action<string> update, string? placeholder = null, bool command = false)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(Label(label));
        panel.Children.Add(Input(value, update, placeholder, command));
        return panel;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = NewButton(text);
        button.Click += (_, _) => action();
        return button;
    }

    private Button AsyncButton(string text, Func<Task> action)
    {
        var button = NewButton(text);
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Log(error.Message); } };
        return button;
    }

    private static Button NewButton(string text) => new()
    {
        Content = text, FontSize = 12, Padding = new Thickness(11, 7),
        Background = Brush.Parse("#1C2A3F"), Foreground = Brush.Parse("#DBE6F7"), BorderBrush = Outline, BorderThickness = new Thickness(1)
    };

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
        _workflow = loaded; _selectedAction = 0; Render(); Log("Workflow opened. Review the commands before running it.");
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
