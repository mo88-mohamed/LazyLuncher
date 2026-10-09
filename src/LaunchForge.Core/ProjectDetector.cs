using System.Text.Json;
using System.Xml.Linq;

namespace LaunchForge.Core;

public sealed record ProjectTask(string Label, string Value);
public sealed record DetectedProject(string Label, ProjectKind Kind, string File, IReadOnlyList<ProjectTask> Tasks);

public static class ProjectDetector
{
    public static IReadOnlyList<ProjectTask> DotNetTasks { get; } = [new("Run application", "run"), new("Run with automatic reload", "watch"), new("Build project", "build"), new("Run tests", "test")];
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", "bin", "obj", ".venv", "venv", ".next", "dist", "artifacts" };

    public static IReadOnlyList<DetectedProject> Detect(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing project folder first.");
        var result = new List<DetectedProject>();
        int visited = 0;
        void Scan(string directory, int depth)
        {
            if (++visited > 200) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.csproj").Take(50))
                {
                    try
                    {
                        var relative = Path.GetRelativePath(folder, file);
                        var document = XDocument.Load(file);
                        if (document.Root?.Name.LocalName != "Project") continue;
                        bool runnable = document.Descendants().Any(element => element.Name.LocalName == "OutputType" && element.Value is "Exe" or "WinExe")
                            || (document.Root?.Attribute("Sdk")?.Value.Contains(".Web", StringComparison.Ordinal) ?? false);
                        var tasks = runnable ? DotNetTasks : DotNetTasks.Where(task => task.Value is "build" or "test").ToArray();
                        result.Add(new($".NET · {relative}", ProjectKind.DotNet, relative, tasks));
                    }
                    catch (System.Xml.XmlException) { }
                }
                var package = Path.Combine(directory, "package.json");
                if (File.Exists(package))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(package));
                        if (document.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var tasks = new List<ProjectTask>();
                            if (document.RootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var script in scripts.EnumerateObject().Where(script => script.Value.ValueKind == JsonValueKind.String))
                                    tasks.Add(new(script.Name switch { "dev" => "Start development server", "start" => "Start application", "build" => "Build project", "test" => "Run tests", _ => script.Name }, script.Name));
                            }
                            var ordered = tasks.OrderBy(task => task.Value == "dev" ? 0 : task.Value == "start" ? 1 : 2).ToArray();
                            result.Add(new($"Node.js · {Path.GetRelativePath(folder, package)}", ProjectKind.Node, Path.GetRelativePath(folder, package), ordered));
                        }
                    }
                    catch (JsonException) { }
                }
                foreach (var file in new[] { "main.py", "app.py", "manage.py" }.Select(name => Path.Combine(directory, name)).Where(File.Exists))
                    result.Add(new($"Python · {Path.GetRelativePath(folder, file)}", ProjectKind.Python, Path.GetRelativePath(folder, file), [new(file.EndsWith("manage.py", StringComparison.OrdinalIgnoreCase) ? "Start Django server" : "Run application", "run")]));
                if (depth >= 2) return;
                foreach (var child in Directory.EnumerateDirectories(directory).Where(child => !Excluded.Contains(Path.GetFileName(child))).Take(100)) Scan(child, depth + 1);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (JsonException) { }
            catch (System.Xml.XmlException) { }
        }
        Scan(Path.GetFullPath(folder), 0);
        return result.OrderBy(project => project.Tasks.Any(task => task.Value is "run" or "dev" or "start") ? 0 : 1)
            .ThenBy(project => project.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
