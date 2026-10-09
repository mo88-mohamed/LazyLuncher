using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchForge.Core;

public sealed class Workflow
{
    public string Name { get; set; } = "My launcher";
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public List<WorkflowStep> Steps { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static Workflow FromJson(string json) =>
        JsonSerializer.Deserialize<Workflow>(json, JsonOptions)
        ?? throw new InvalidDataException("The workflow file is empty.");

    public void Validate(bool windows)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Give your workflow a name.");
        if (Steps is null || Steps.Count == 0) throw new InvalidDataException("Add at least one action.");
        ValidateDirectory(WorkingDirectory);
        foreach (var step in Steps)
        {
            if (step is null) throw new InvalidDataException("An action is missing.");
            if (string.IsNullOrWhiteSpace(step.CommandFor(windows)))
                throw new InvalidDataException($"Enter a {(windows ? "Windows" : "Linux")} command for '{step.Name}'.");
            if (windows && step.CommandFor(true).IndexOfAny(['\r', '\n']) >= 0)
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
    public string Name { get; set; } = "Action";
    public string WindowsCommand { get; set; } = "";
    public string LinuxCommand { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool WaitForExit { get; set; } = true;
    public string CommandFor(bool windows) => windows ? WindowsCommand : LinuxCommand;
}
