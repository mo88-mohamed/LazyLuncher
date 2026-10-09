using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchForge.Core;

public sealed class Workflow
{
    public string Name { get; set; } = "My launcher";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static Workflow FromJson(string json)
    {
        var workflow = JsonSerializer.Deserialize<Workflow>(json, JsonOptions) ?? throw new InvalidDataException("The workflow file is empty.");
        workflow.Name ??= ""; workflow.WorkingDirectory ??= ""; workflow.IconPath ??= "";
        if (workflow.Steps is not null)
            foreach (var step in workflow.Steps)
            {
                if (step is null) continue;
                step.Name ??= "Action"; step.WorkingDirectory ??= ""; step.WindowsCommand ??= ""; step.LinuxCommand ??= "";
                step.Target ??= ""; step.ProjectFile ??= ""; step.ProjectTask ??= "";
            }
        return workflow;
    }

    public string ProjectFolderFor(WorkflowStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.WorkingDirectory)) return step.WorkingDirectory;
        if (step.Kind == ActionKind.RunProject)
        {
            var index = Steps.IndexOf(step);
            var previous = Steps.Take(Math.Max(0, index)).LastOrDefault(item => item.Kind == ActionKind.OpenVsCode);
            if (previous is not null)
                return !string.IsNullOrWhiteSpace(previous.Target) ? previous.Target
                    : !string.IsNullOrWhiteSpace(previous.WorkingDirectory) ? previous.WorkingDirectory : WorkingDirectory;
        }
        return WorkingDirectory;
    }

    public void Validate(bool windows)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Give your workflow a name.");
        if (Steps is null || Steps.Count == 0) throw new InvalidDataException("Add at least one action.");
        foreach (var step in Steps)
        {
            if (step is null) throw new InvalidDataException("An action is missing.");
            if (!Enum.IsDefined(step.Kind) || !Enum.IsDefined(step.ProjectKind) || !Enum.IsDefined(step.RunIn))
                throw new InvalidDataException("This workflow contains an unsupported action type.");
            var folder = ProjectFolderFor(step);
            if (step.Kind == ActionKind.RunProject && string.IsNullOrWhiteSpace(folder))
                throw new InvalidDataException($"Choose the project folder for '{step.Name}', or set it in the preceding VS Code action.");
            ValidateDirectory(step.ExecutionFolder(folder));
            var command = step.CommandFor(windows, folder);
            if (string.IsNullOrWhiteSpace(command))
                throw new InvalidDataException($"Enter a {(windows ? "Windows" : "Linux")} command for '{step.Name}'.");
            if (windows && command.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidDataException("Windows commands must fit on one line. Use && to join commands.");
            if (!string.IsNullOrWhiteSpace(step.WorkingDirectory)) ValidateDirectory(step.WorkingDirectory);
        }
    }

    private static void ValidateDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Contains('\0') || directory.Contains('\n') || directory.Contains('\r'))
            throw new InvalidDataException("Enter a valid working folder.");
    }
}

public sealed class WorkflowStep
{
    // Keep CustomCommand as zero so older workflow files retain their behavior.
    public ActionKind Kind { get; set; } = ActionKind.CustomCommand;
    public string Name { get; set; } = "Action";
    public string WindowsCommand { get; set; } = "";
    public string LinuxCommand { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool WaitForExit { get; set; } = true;
    public string Target { get; set; } = "";
    public ProjectKind ProjectKind { get; set; } = ProjectKind.Auto;
    public string ProjectFile { get; set; } = "";
    public string ProjectTask { get; set; } = "";
    public RunLocation RunIn { get; set; }
    [JsonIgnore] public bool UsesVsCodeIntegration => RunIn == RunLocation.VsCode && Kind is ActionKind.RunProject or ActionKind.CustomCommand;
    public string ExecutionFolder(string defaultFolder) => !string.IsNullOrWhiteSpace(WorkingDirectory) ? WorkingDirectory
        : Kind is ActionKind.OpenWebsite or ActionKind.OpenApplication || (Kind is ActionKind.OpenVsCode or ActionKind.OpenFolder && !string.IsNullOrWhiteSpace(Target)) ? "." : defaultFolder;
    public string CommandFor(bool windows, string? defaultFolder = null) => GuidedActions.CommandFor(this, windows, defaultFolder ?? Environment.CurrentDirectory);
}

public enum ActionKind { CustomCommand, OpenVsCode, RunProject, OpenWebsite, OpenFolder, OpenApplication }
public enum ProjectKind { Auto, DotNet, Node, Python, JavaScript }
public enum RunLocation { External, VsCode }
