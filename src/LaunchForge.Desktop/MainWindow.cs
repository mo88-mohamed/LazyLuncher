using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly Grid _editor = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(18, 10, 18, 0) };
    private readonly StackPanel _details = new() { Spacing = 10 };
    private ScrollViewer? _detailScroll;
    private readonly List<TextBlock> _actionLabels = [];
    private readonly TextBox _status = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 36, MaxHeight = 72, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 12, Foreground = Muted, Text = "Ready. Choose a project folder and configure your actions." };
    private readonly Button _cancel = new() { Content = "Cancel", IsVisible = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _operation;
    private ExportFormat _format = OperatingSystem.IsWindows() ? ExportFormat.WindowsBatch : ExportFormat.LinuxShell;
    private int _selectedAction;
    private int _commandTab = OperatingSystem.IsWindows() ? 0 : 1;
    private readonly Dictionary<string, IReadOnlyList<DetectedProject>> _projects = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly (ActionKind Kind, string Label)[] ActionTypes =
    [
        (ActionKind.RunProject, "Run a project"), (ActionKind.OpenVsCode, "Open project in VS Code"),
        (ActionKind.OpenWebsite, "Open a website"), (ActionKind.OpenFolder, "Open a folder"),
        (ActionKind.OpenApplication, "Open an application"), (ActionKind.CustomCommand, "Custom command")
    ];

    public MainWindow()
    {
        Title = "Lazy Launcher";
        Width = 820; Height = 640; MinWidth = 640; MinHeight = 480;
        Background = Brush.Parse("#0B1220");
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 0;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var titleBar = CreateTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top); root.Children.Add(titleBar);
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

    private Control CreateTitleBar()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 32, Background = Brush.Parse("#0B1220") };
        var dragArea = new Border { Background = Brushes.Transparent, Child = new TextBlock { Text = "Lazy Launcher", FontSize = 11, Foreground = Muted, Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center } };
        void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        dragArea.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (args.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(args);
            args.Handled = true;
        };
        bar.Children.Add(dragArea);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        Button Caption(string symbol, string label, Action action)
        {
            var button = new Button { Content = symbol, Width = 44, Height = 32, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), Foreground = Muted, FontSize = 16, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(button, label);
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => action();
            controls.Children.Add(button); return button;
        }
        Caption("\u2212", "Minimize", () => WindowState = WindowState.Minimized);
        var maximize = Caption("\u25a1", "Maximize", ToggleMaximize);
        PropertyChanged += (_, args) =>
        {
            if (args.Property != WindowStateProperty) return;
            var label = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            maximize.Content = WindowState == WindowState.Maximized ? "\u2750" : "\u25a1";
            ToolTip.SetTip(maximize, label); Avalonia.Automation.AutomationProperties.SetName(maximize, label);
        };
        var close = Caption("\u00d7", "Close", Close);
        close.PointerEntered += (_, _) => { close.Background = Brush.Parse("#B42335"); close.Foreground = Brushes.White; };
        close.PointerExited += (_, _) => { close.Background = Brushes.Transparent; close.Foreground = Muted; };
        Grid.SetColumn(controls, 1); bar.Children.Add(controls);
        return bar;
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
        WorkingDirectory = "",
        Steps =
        [
            new() { Kind = ActionKind.OpenVsCode, Name = "Open project in VS Code" },
            new() { Kind = ActionKind.RunProject, Name = "Run project", RunIn = RunLocation.VsCode }
        ]
    };

    private void Render()
    {
        // The editor is reused when the workflow or action order changes.
        if (_detailScroll is not null) _detailScroll.Content = null;
        _editor.Children.Clear();
        var workflow = _workflow;
        _selectedAction = Math.Clamp(_selectedAction, 0, Math.Max(0, _workflow.Steps.Count - 1));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        brand.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Brush.Parse("#153D40"),
            Child = new TextBlock { Text = "LL", Foreground = Accent, FontWeight = FontWeight.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var title = new StackPanel { Spacing = 1 };
        title.Children.Add(new TextBlock { Text = "Lazy Launcher", FontSize = 21, FontWeight = FontWeight.Bold });
        title.Children.Add(Hint("Your tasks. One click."));
        brand.Children.Add(title);
        header.Children.Add(brand);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(ActionButton("New", () => { _workflow = new Workflow(); _selectedAction = 0; Render(); }));
        toolbar.Children.Add(AsyncButton("Open", OpenAsync));
        var save = AsyncButton("Save workflow", SaveAsync);
        ToolTip.SetTip(save, "Save the launcher name, settings, icon and actions as a workflow JSON file.");
        toolbar.Children.Add(save);
        Grid.SetColumn(toolbar, 1); header.Children.Add(toolbar);
        _editor.Children.Add(header);

        var setup = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        var name = new StackPanel { Spacing = 5 };
        name.Children.Add(Label("Launcher name"));
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        nameRow.Children.Add(Input(workflow.Name, value => workflow.Name = value));
        var icon = CreateIconButton(workflow);
        icon.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(icon, 1); nameRow.Children.Add(icon);
        name.Children.Add(nameRow);
        name.Margin = new Thickness(0, 0, 12, 0); setup.Children.Add(name);
        var folderGroup = new StackPanel { Spacing = 8, Width = 380, MaxWidth = 380 };
        folderGroup.Children.Add(new TextBlock { Text = "Workflow settings", FontSize = 16, FontWeight = FontWeight.SemiBold });
        folderGroup.Children.Add(Label("Shared working folder (optional)"));
        folderGroup.Children.Add(Hint("Fallback folder for actions without their own folder. Run project first uses the preceding VS Code action's folder."));
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var folderInput = Input(workflow.WorkingDirectory, value => { workflow.WorkingDirectory = value; ResetInheritedProjects(); }, "Choose a shared folder, or leave empty");
        ToolTip.SetTip(folderInput, "Full folder path on the computer that will run the launcher. Saved inside the workflow.");
        folderRow.Children.Add(folderInput);
        var browse = AsyncButton("Browse", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                workflow.WorkingDirectory = path;
                folderInput.Text = path;
                var projectStep = workflow.Steps.FirstOrDefault(step => step.Kind == ActionKind.RunProject && string.IsNullOrWhiteSpace(step.WorkingDirectory));
                if (projectStep is not null) await DetectProjectsAsync(projectStep, applyToAll: true);
            }
        });
        browse.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);
        folderGroup.Children.Add(folderRow);
        folderGroup.Children.Add(Hint("Save workflow remembers this setting. You choose where to save the workflow or generated launcher separately."));
        var settings = NewButton("Workflow settings");
        settings.VerticalAlignment = VerticalAlignment.Bottom;
        settings.Flyout = new Flyout { Content = folderGroup };
        ToolTip.SetTip(settings, "Optional shared working folder. Each action can choose its own folder.");
        Grid.SetColumn(settings, 1); setup.Children.Add(settings);
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
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0 && _format != formats[choice.SelectedIndex]) { _format = formats[choice.SelectedIndex]; Render(); } };
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

    private Button CreateIconButton(Workflow workflow)
    {
        var icon = NewButton(string.IsNullOrWhiteSpace(workflow.IconPath) ? "Icon" : "Icon \u2713");
        icon.IsEnabled = _format is ExportFormat.WindowsBatch or ExportFormat.WindowsExecutable;
        if (!string.IsNullOrWhiteSpace(workflow.IconPath)) icon.Foreground = Accent;
        ToolTip.SetTip(icon, icon.IsEnabled ? (string.IsNullOrWhiteSpace(workflow.IconPath) ? "Choose a custom launcher icon" : workflow.IconPath) : "Custom icons are available for Windows exports.");
        var iconMenu = new StackPanel { Spacing = 10, MaxWidth = 290 };
        iconMenu.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(workflow.IconPath) ? "Default launcher icon" : Path.GetFileName(workflow.IconPath), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var iconFlyout = new Flyout { Content = iconMenu };
        iconMenu.Children.Add(AsyncButton("Choose .ico file...", async () => { iconFlyout.Hide(); await PickIconAsync(); }));
        var resetIcon = ActionButton("Use default icon", () => { workflow.IconPath = ""; iconFlyout.Hide(); Render(); });
        resetIcon.IsEnabled = !string.IsNullOrWhiteSpace(workflow.IconPath); iconMenu.Children.Add(resetIcon);
        iconMenu.Children.Add(Hint(_format == ExportFormat.WindowsBatch ? "Creates a shortcut beside the batch file with your icon." : "Embeds your icon directly into the exported EXE."));
        icon.Flyout = iconFlyout;
        return icon;
    }

    private static string ActionTitle(int index, WorkflowStep step) => $"{index + 1:00}   {(string.IsNullOrWhiteSpace(step.Name) ? "Untitled action" : step.Name)}";

    private void AddAction()
    {
        _workflow.Steps.Add(new WorkflowStep { Kind = ActionKind.RunProject, Name = "Run project" });
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
        var kind = new ComboBox { ItemsSource = ActionTypes.Select(item => item.Label).ToArray(), SelectedIndex = Array.FindIndex(ActionTypes, item => item.Kind == step.Kind), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13 };
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0 || step.Kind == ActionTypes[kind.SelectedIndex].Kind) return;
            var oldLabel = ActionTypes.First(item => item.Kind == step.Kind).Label;
            step.Kind = ActionTypes[kind.SelectedIndex].Kind;
            if (step.Name is "Run project" or "Action" || step.Name == oldLabel) { step.Name = ActionTypes[kind.SelectedIndex].Label; _actionLabels[position].Text = ActionTitle(position, step); }
            RenderDetails();
        };
        _details.Children.Add(Labeled("Action", kind));
        var actionLabel = _actionLabels[position];
        Control? taskChoice = null;
        if (step.Kind == ActionKind.RunProject) taskChoice = RenderProjectOptions(step);
        else if (step.Kind == ActionKind.CustomCommand) RenderCustomCommands(step);
        else if (step.Kind == ActionKind.OpenWebsite) _details.Children.Add(Field("Website address", step.Target, value => step.Target = value, "https://localhost:5001"));
        else
        {
            bool application = step.Kind == ActionKind.OpenApplication;
            _details.Children.Add(Field(application ? "Application" : "Folder", step.Target, value => { step.Target = value; if (step.Kind == ActionKind.OpenVsCode) ResetInheritedProjects(); }, application ? "Select an application or enter its name" : "Leave blank to use the shared folder in Workflow settings"));
            _details.Children.Add(AsyncButton(application ? "Browse application..." : "Browse folder...", async () =>
            {
                if (application)
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose application", AllowMultiple = false });
                    if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) step.Target = path;
                }
                else
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose folder", AllowMultiple = false });
                    if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                    {
                        step.Target = path;
                        if (step.Kind == ActionKind.OpenVsCode) ResetInheritedProjects();
                        var nextProject = _workflow.Steps.Skip(position + 1).FirstOrDefault(item => item.Kind == ActionKind.RunProject && string.IsNullOrWhiteSpace(item.WorkingDirectory));
                        if (step.Kind == ActionKind.OpenVsCode && nextProject is not null) await DetectProjectsAsync(nextProject, applyToAll: true);
                    }
                }
                RenderDetails();
            }));
        }
        if (step.Kind is ActionKind.RunProject or ActionKind.CustomCommand)
        {
            var location = new ComboBox { ItemsSource = new[] { "Outside VS Code", "VS Code terminal" }, SelectedIndex = (int)step.RunIn, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            location.SelectionChanged += (_, _) => { if (location.SelectedIndex >= 0 && (int)step.RunIn != location.SelectedIndex) { step.RunIn = (RunLocation)location.SelectedIndex; RenderDetails(); } };
            var locationField = Labeled("Run in", location);
            if (taskChoice is null) _details.Children.Add(locationField);
            else
            {
                var taskRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                taskChoice.Margin = new Thickness(0, 0, 10, 0); taskRow.Children.Add(taskChoice);
                Grid.SetColumn(locationField, 1); taskRow.Children.Add(locationField); _details.Children.Add(taskRow);
            }
        }
        if (step.UsesVsCodeIntegration)
            _details.Children.Add(Hint("First use sets up the VS Code integration and asks you to approve the task. Later actions do not wait for this task to finish."));
        else
        {
            var wait = new CheckBox { Content = "Wait before starting the next action", IsChecked = step.WaitForExit, FontSize = 12 };
            wait.IsCheckedChanged += (_, _) => step.WaitForExit = wait.IsChecked == true;
            ToolTip.SetTip(wait, "When enabled, stop the workflow if this action fails. When disabled, the action keeps running independently.");
            _details.Children.Add(wait);
        }
        var advanced = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        advanced.Children.Add(Field("Action name", step.Name, value => { step.Name = value; actionLabel.Text = ActionTitle(position, step); }));
        if (step.Kind != ActionKind.RunProject) advanced.Children.Add(Field("Working folder", step.WorkingDirectory, value => step.WorkingDirectory = value, "Leave blank to use the shared folder in Workflow settings"));
        _details.Children.Add(new Expander { Header = "Advanced options", Content = advanced, IsExpanded = !string.IsNullOrWhiteSpace(step.WorkingDirectory), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Background = Surface, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) });
    }

    private void RenderCustomCommands(WorkflowStep step)
    {
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
        _details.Children.Add(Hint(step.UsesVsCodeIntegration ? "Uses your configured VS Code terminal shell." : "Windows uses cmd.exe; Linux uses Bash."));
    }

    private void ResetInheritedProjects()
    {
        foreach (var step in _workflow.Steps.Where(item => item.Kind == ActionKind.RunProject && string.IsNullOrWhiteSpace(item.WorkingDirectory)))
        {
            step.ProjectKind = ProjectKind.Auto; step.ProjectFile = ""; step.ProjectTask = "";
        }
    }

    private Control? RenderProjectOptions(WorkflowStep step)
    {
        var folder = _workflow.ProjectFolderFor(step);
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        folderRow.Children.Add(Input(step.WorkingDirectory, value => { step.WorkingDirectory = value; step.ProjectKind = ProjectKind.Auto; step.ProjectFile = ""; step.ProjectTask = ""; },
            string.IsNullOrWhiteSpace(folder) ? "Choose the folder containing your project" : folder));
        var browse = AsyncButton("Browse", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose project to run", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                step.WorkingDirectory = path; step.ProjectKind = ProjectKind.Auto; step.ProjectFile = ""; step.ProjectTask = "";
                await DetectProjectsAsync(step);
            }
        });
        browse.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);
        _details.Children.Add(Labeled("Project folder for this action", folderRow));
        _details.Children.Add(Hint(string.IsNullOrWhiteSpace(step.WorkingDirectory)
            ? "Uses the preceding VS Code folder, then Workflow settings. Browse to override."
            : "Uses this action's folder. Clear it to follow the preceding VS Code action."));
        if (_projects.TryGetValue(folder, out var detected) && detected.Count > 0)
        {
            int index = detected.ToList().FindIndex(project => project.Kind == step.ProjectKind && project.File == step.ProjectFile);
            var projectChoice = new ComboBox { ItemsSource = detected.Select(project => project.Label).ToArray(), SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, PlaceholderText = "Choose a detected project" };
            projectChoice.SelectionChanged += (_, _) =>
            {
                if (projectChoice.SelectedIndex < 0) return;
                if (_workflow.ProjectFolderFor(step) != folder) { RenderDetails(); return; }
                var project = detected[projectChoice.SelectedIndex];
                if (step.ProjectKind == project.Kind && step.ProjectFile == project.File) return;
                ApplyProject(step, project); RenderDetails();
            };
            var projectRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            projectRow.Children.Add(projectChoice);
            var refresh = AsyncButton("Detect", () => DetectProjectsAsync(step)); refresh.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(refresh, 1); projectRow.Children.Add(refresh);
            _details.Children.Add(Labeled("Project", projectRow));
            if (index >= 0) return TaskChoice(step, detected[index].Tasks);
        }
        else
        {
            _details.Children.Add(AsyncButton("Detect project", () => DetectProjectsAsync(step)));
            _details.Children.Add(Hint("Detect package.json tasks or JavaScript entry files, .NET projects, and Python apps."));
            var type = new ComboBox { ItemsSource = new[] { "Detect automatically", ".NET", "JavaScript / Node.js (npm tasks)", "Python", "JavaScript file (Node.js)" }, SelectedIndex = (int)step.ProjectKind, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            type.SelectionChanged += (_, _) => { if (type.SelectedIndex >= 0 && (int)step.ProjectKind != type.SelectedIndex) { step.ProjectKind = (ProjectKind)type.SelectedIndex; step.ProjectTask = ""; RenderDetails(); } };
            _details.Children.Add(Labeled("Project type", type));
            if (step.ProjectKind != ProjectKind.Auto)
                _details.Children.Add(Field("Project file (relative to project folder)", step.ProjectFile, value => step.ProjectFile = value, step.ProjectKind == ProjectKind.DotNet ? "MyApp.csproj (optional)" : step.ProjectKind == ProjectKind.Node ? "package.json (optional)" : step.ProjectKind == ProjectKind.JavaScript ? "index.js, app.mjs or server.cjs" : "main.py"));
            if (step.ProjectKind == ProjectKind.DotNet) return TaskChoice(step, ProjectDetector.DotNetTasks);
            if (step.ProjectKind == ProjectKind.Node) return Field("Task name", step.ProjectTask, value => step.ProjectTask = value, "dev or start");
            if (step.ProjectKind is ProjectKind.JavaScript or ProjectKind.Python) return TaskChoice(step, [new("Run application", "run")]);
        }
        return null;
    }

    private static Control TaskChoice(WorkflowStep step, IReadOnlyList<ProjectTask> tasks)
    {
        if (tasks.Count == 0) return Hint("No runnable tasks found. Use Custom command to define one.");
        int index = tasks.ToList().FindIndex(task => task.Value == step.ProjectTask);
        if (index < 0) { index = 0; step.ProjectTask = tasks[0].Value; }
        var choice = new ComboBox { ItemsSource = tasks.Select(task => task.Label).ToArray(), SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) step.ProjectTask = tasks[choice.SelectedIndex].Value; };
        return Labeled("Task", choice);
    }

    private async Task DetectProjectsAsync(WorkflowStep step, bool applyToAll = false)
    {
        var workflow = _workflow;
        var folder = workflow.ProjectFolderFor(step);
        if (string.IsNullOrWhiteSpace(folder)) { Log("Choose the project folder for this action, or set a folder in the preceding VS Code action."); return; }
        await WithOperationAsync(async token =>
        {
            Log("Detecting projects...");
            var detected = await Task.Run(() => ProjectDetector.Detect(folder), token);
            token.ThrowIfCancellationRequested();
            _projects[folder] = detected;
            if (detected.Count > 0)
            {
                foreach (var candidate in applyToAll ? workflow.Steps.Where(item => item.Kind == ActionKind.RunProject && string.Equals(workflow.ProjectFolderFor(item), folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) : [step])
                    ApplyProject(candidate, detected.FirstOrDefault(project => project.Kind == candidate.ProjectKind && project.File == candidate.ProjectFile) ?? detected[0]);
            }
            RenderDetails();
            Log(detected.Count == 0 ? "No supported project found. Choose a project type or use Custom command." : $"Found {detected.Count} project(s). Select your project and task.");
        });
    }

    private static void ApplyProject(WorkflowStep step, DetectedProject project)
    {
        step.ProjectKind = project.Kind; step.ProjectFile = project.File;
        if (!project.Tasks.Any(task => task.Value == step.ProjectTask)) step.ProjectTask = project.Tasks.FirstOrDefault()?.Value ?? "";
    }

    private static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 5 }; panel.Children.Add(Label(label)); panel.Children.Add(control); return panel;
    }

    private async Task PickIconAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose launcher icon", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Windows icon") { Patterns = ["*.ico"] }] });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath() ?? throw new IOException("Choose a local icon file.");
        LauncherIcon.Validate(path); _workflow.IconPath = path; Render(); Log("Custom icon selected.");
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open workflow", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Lazy Launcher workflow") { Patterns = ["*.json"] }] });
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
        await writer.WriteAsync(_workflow.ToJson()); Log($"Workflow saved with its settings and actions: {file.Name}");
    }

    private string SafeName() => string.Concat(_workflow.Name.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')).Trim('-') is { Length: > 0 } name ? name : "launcher";

    private async Task ExportAsync()
    {
        _workflow.Validate(_format is ExportFormat.WindowsBatch or ExportFormat.WindowsExecutable);
        var extension = _format switch { ExportFormat.WindowsBatch => ".bat", ExportFormat.LinuxShell => ".sh", ExportFormat.WindowsExecutable => ".exe", _ => "" };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export launcher", SuggestedFileName = SafeName() + extension, DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException("Choose a local destination for the launcher.");
        await WithOperationAsync(async token =>
        {
            await LauncherExporter.ExportAsync(_workflow, _format, path, Log, token);
            Log(_format == ExportFormat.WindowsBatch && !string.IsNullOrWhiteSpace(_workflow.IconPath) ? $"Exported batch and shortcut with your icon: {Path.ChangeExtension(path, ".lnk")}" : $"Launcher exported: {path}");
        });
    }
}
