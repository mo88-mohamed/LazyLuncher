using System.Text;
using System.Text.Json;

namespace LaunchForge.Core;

public static class GuidedActions
{
    public static string ShellQuote(string text) => "'" + text.Replace("'", "'\"'\"'") + "'";
    public static string PowerShellQuote(string text) => "'" + text.Replace("'", "''") + "'";
    public static string WindowsScript(string script) => "powershell.exe -NoLogo -NoProfile -OutputFormat Text -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));

    public static string CommandFor(WorkflowStep step, bool windows, string defaultFolder)
    {
        var folder = string.IsNullOrWhiteSpace(step.WorkingDirectory) ? defaultFolder : step.WorkingDirectory;
        if (step.Kind == ActionKind.CustomCommand && !step.UsesVsCodeIntegration) return windows ? step.WindowsCommand : step.LinuxCommand;
        if (step.Kind == ActionKind.OpenVsCode) return Tool("code", [string.IsNullOrWhiteSpace(step.Target) ? "." : step.Target], windows);
        if (step.Kind is ActionKind.OpenWebsite or ActionKind.OpenFolder)
        {
            var target = step.Kind == ActionKind.OpenFolder && string.IsNullOrWhiteSpace(step.Target) ? folder : step.Target;
            if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException($"Choose a destination for '{step.Name}'.");
            if (step.Kind == ActionKind.OpenWebsite && (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                throw new InvalidDataException("Enter a website address starting with https:// or http://.");
            return windows ? WindowsScript($"Start-Process -FilePath {PowerShellQuote(target)}") : Tool("xdg-open", [target], false);
        }
        if (step.Kind == ActionKind.OpenApplication)
        {
            if (string.IsNullOrWhiteSpace(step.Target)) throw new InvalidDataException("Choose an application to open.");
            return windows ? WindowsScript($"Start-Process -FilePath {PowerShellQuote(step.Target)}") : Tool(step.Target, [], false);
        }
        string executable = "";
        string[] arguments = [];
        string? custom = null;
        if (step.Kind == ActionKind.CustomCommand)
        {
            custom = windows ? step.WindowsCommand : step.LinuxCommand;
            if (string.IsNullOrWhiteSpace(custom)) throw new InvalidDataException($"Enter a command for '{step.Name}'.");
        }
        else if (step.Kind == ActionKind.RunProject)
        {
            var kind = step.ProjectKind;
            var projectFile = step.ProjectFile;
            var task = step.ProjectTask;
            if (kind == ProjectKind.Auto)
            {
                var detected = ProjectDetector.Detect(folder);
                if (detected.Count != 1) throw new InvalidDataException(detected.Count == 0 ? "No project found. Use Detect project or choose a project type." : "Several projects were found. Use Detect project and choose which one to run.");
                kind = detected[0].Kind; projectFile = detected[0].File;
                if (string.IsNullOrWhiteSpace(task)) task = detected[0].Tasks.FirstOrDefault()?.Value ?? "";
            }
            projectFile = windows ? projectFile.Replace('/', '\\') : projectFile.Replace('\\', '/');
            if (kind == ProjectKind.DotNet)
            {
                task = string.IsNullOrWhiteSpace(task) ? "run" : task;
                if (task is not ("run" or "watch" or "build" or "test")) throw new InvalidDataException("Choose a supported .NET task.");
                executable = "dotnet";
                arguments = task == "watch" ? ["watch", "run"] : [task];
                if (!string.IsNullOrWhiteSpace(projectFile)) arguments = task switch
                {
                    "watch" => ["watch", "--project", projectFile, "run"],
                    "build" or "test" => [.. arguments, projectFile],
                    _ => [.. arguments, "--project", projectFile]
                };
            }
            else if (kind == ProjectKind.Node)
            {
                if (string.IsNullOrWhiteSpace(task)) throw new InvalidDataException("Choose a task from package.json, such as Start application.");
                executable = "npm";
                var projectDirectory = string.IsNullOrWhiteSpace(projectFile) ? "" : (windows ? projectFile.Replace('/', '\\') : projectFile.Replace('\\', '/'));
                var separator = projectDirectory.LastIndexOf(windows ? '\\' : '/');
                arguments = separator >= 0 ? ["--prefix", projectDirectory[..separator], "run", task] : ["run", task];
            }
            else if (kind == ProjectKind.Python)
            {
                if (string.IsNullOrWhiteSpace(projectFile)) throw new InvalidDataException("Choose a Python entry file, such as main.py.");
                executable = windows ? "python" : "python3";
                arguments = projectFile.EndsWith("manage.py", StringComparison.OrdinalIgnoreCase) ? [projectFile, "runserver"] : [projectFile];
            }
            else throw new InvalidDataException("Choose a supported project type.");
        }
        else throw new InvalidDataException("Unsupported action.");

        if (step.UsesVsCodeIntegration)
        {
            bool fullFolder = windows ? (folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] is '\\' or '/') || folder.StartsWith("\\\\", StringComparison.Ordinal) : folder.StartsWith('/');
            if (!fullFolder) throw new InvalidDataException("Use a full project folder path for VS Code tasks.");
            var request = JsonSerializer.Serialize(new { version = 1, folder, name = step.Name, executable, args = arguments, command = custom });
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(request)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var url = "vscode://launchforge.launcher/run?request=" + encoded;
            return Tool("code", ["--open-url", url], windows);
        }
        return Tool(executable, arguments, windows);
    }

    private static string Tool(string executable, string[] arguments, bool windows) => windows
        ? WindowsScript($"& {PowerShellQuote(executable)} {string.Join(" ", arguments.Select(PowerShellQuote))}; if ($null -ne $LASTEXITCODE) {{ exit $LASTEXITCODE }}")
        : ShellQuote(executable) + " " + string.Join(" ", arguments.Select(ShellQuote));
}
