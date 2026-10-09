using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LaunchForge.Core;

// Dependency-free behavioral checks. All executed commands write only to a temporary folder.
var scratch = Path.Combine(Path.GetTempPath(), "LaunchForge-checks-" + Guid.NewGuid().ToString("N"), "space & apostrophe's %folder%");
Directory.CreateDirectory(scratch);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); checks++;
}
Workflow Make(params WorkflowStep[] steps) => new() { Name = "Test", WorkingDirectory = scratch, Steps = [.. steps] };
WorkflowStep Command(string windows, string linux) => new() { Name = "Check", WindowsCommand = windows, LinuxCommand = linux };
try
{
    if (args.FirstOrDefault() == "/Run")
    {
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("LAUNCHFORGE_VS_TEST_LOG")!, args[1]);
        return 0;
    }
    var workflow = Make(Command("echo first>first.txt", "printf first > first.txt"), Command("if not exist first.txt exit /b 9", "test -f first.txt"));
    Check(Workflow.FromJson(workflow.ToJson()).Steps.Count == 2, "Workflow JSON round trip");
    Check(await WorkflowRunner.RunAsync(workflow) == 0 && File.Exists(Path.Combine(scratch, "first.txt")), "Ordered commands and working directory");
    var fail = Make(Command("exit /b 7", "exit 7"), Command("echo bad>should-not-exist.txt", "touch should-not-exist.txt"));
    Check(await WorkflowRunner.RunAsync(fail) == 7 && !File.Exists(Path.Combine(scratch, "should-not-exist.txt")), "Failure stops the next action and preserves exit code");
    try { Make().Validate(true); Check(false, "Empty workflow rejected"); } catch (InvalidDataException) { Check(true, "Empty workflow rejected"); }
    try { Make(Command("", "echo ok")).Validate(true); Check(false, "Missing target command rejected"); } catch (InvalidDataException) { Check(true, "Missing target command rejected"); }
    var shell = LauncherExporter.CreateShell(Make(Command("echo ok", "printf '%s' \"a'b & c\" > shell.txt")));
    Check(shell.StartsWith("#!/usr/bin/env bash\n") && !shell.Contains('\r') && shell.Contains("'\"'\"'"), "Shell export uses LF and escapes apostrophes");
    var format = OperatingSystem.IsWindows() ? ExportFormat.WindowsBatch : ExportFormat.LinuxShell;
    var launcher = Path.Combine(scratch, OperatingSystem.IsWindows() ? "success.bat" : "success.sh");
    var exported = Make(Command("echo exported>exported.txt", "printf exported > exported.txt"));
    await LauncherExporter.ExportAsync(exported, format, launcher);
    async Task<int> Execute(string path)
    {
        var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (OperatingSystem.IsWindows()) info.Arguments = "/d /s /c \"\"" + path + "\"\"";
        else info.ArgumentList.Add(path);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = await output + await errors;
        if (process.ExitCode != 0 && process.ExitCode != 7) Console.Error.WriteLine(text.Length > 2000 ? text[^2000..] : text);
        return process.ExitCode;
    }
    Check(await Execute(launcher) == 0 && File.Exists(Path.Combine(scratch, "exported.txt")), "Exported script runs with spaces, apostrophe, ampersand and percent in path");
    await LauncherExporter.ExportAsync(fail, format, launcher);
    Check(await Execute(launcher) == 7 && !File.Exists(Path.Combine(scratch, "should-not-exist.txt")), "Exported script stops on failure");
    var cancellation = Make(Command("ping -n 30 127.0.0.1 >nul", "sleep 30"), Command("echo bad>cancelled.txt", "touch cancelled.txt"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    try { await WorkflowRunner.RunAsync(cancellation, cancellationToken: timeout.Token); Check(false, "Cancellation stops the workflow"); }
    catch (OperationCanceledException) { Check(!File.Exists(Path.Combine(scratch, "cancelled.txt")), "Cancellation stops the workflow"); }
    var iconPath = Path.Combine(scratch, "custom icon.ico");
    var iconBytes = IconFixture.Create();
    await File.WriteAllBytesAsync(iconPath, iconBytes);
    LauncherIcon.Validate(iconPath);
    exported.IconPath = iconPath;
    Check(Workflow.FromJson(exported.ToJson()).IconPath == iconPath, "Selected icon survives workflow save/load");
    var brokenIcon = Path.Combine(scratch, "broken.ico");
    await File.WriteAllTextAsync(brokenIcon, "invalid icon contents long enough");
    try { LauncherIcon.Validate(brokenIcon); Check(false, "Invalid icon rejected"); } catch (InvalidDataException) { Check(true, "Invalid icon rejected"); }
    var projectRoot = Path.Combine(scratch, "projects"); Directory.CreateDirectory(projectRoot);
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "Broken.csproj"), "invalid XML");
    Directory.CreateDirectory(Path.Combine(projectRoot, "frontend"));
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "frontend", "package.json"), "{\"scripts\":{\"start\":\"node app.js\",\"dev\":\"vite\",\"test\":\"node --test\"}}");
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "main.py"), "print('example')");
    Directory.CreateDirectory(Path.Combine(projectRoot, "node_modules", "ignored"));
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "node_modules", "ignored", "package.json"), "{\"scripts\":{\"start\":\"bad\"}}");
    var detected = ProjectDetector.Detect(projectRoot);
    Check(detected.Count == 3 && detected.Any(project => project.Kind == ProjectKind.DotNet) && detected.Any(project => project.Kind == ProjectKind.Python), "Detects .NET, Node and Python while excluding dependencies");
    Check(detected.Single(project => project.Kind == ProjectKind.Node).Tasks[0].Value == "dev", "Node task detection prefers the development server");
    var visualStudioStep = new WorkflowStep { Kind = ActionKind.RunProject, ProjectKind = ProjectKind.DotNet, ProjectFile = "App.csproj", ProjectTask = "run", WorkingDirectory = projectRoot, RunIn = RunLocation.VisualStudio };
    var visualStudioWorkflow = Make(visualStudioStep);
    var visualStudioScript = visualStudioStep.CommandFor(true, projectRoot);
    var decodedVisualStudioScript = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(visualStudioScript[(visualStudioScript.LastIndexOf(' ') + 1)..]));
    Check(decodedVisualStudioScript.Contains("vswhere") && decodedVisualStudioScript.Contains("'/Run'") && decodedVisualStudioScript.Contains("App.csproj"), "Visual Studio auto-discovery and Run arguments are generated");
    try { visualStudioWorkflow.Validate(false); Check(false, "Visual Studio rejects Linux exports"); } catch (InvalidDataException) { Check(true, "Visual Studio rejects Linux exports"); }
    visualStudioStep.ProjectKind = ProjectKind.Node;
    try { visualStudioWorkflow.Validate(true); Check(false, "Visual Studio rejects non-.NET projects"); } catch (InvalidDataException) { Check(true, "Visual Studio rejects non-.NET projects"); }
    visualStudioStep.ProjectKind = ProjectKind.DotNet; visualStudioStep.ProjectTask = "watch";
    try { visualStudioWorkflow.Validate(true); Check(false, "Visual Studio rejects unsupported tasks"); } catch (InvalidDataException) { Check(true, "Visual Studio rejects unsupported tasks"); }
    visualStudioStep.ProjectTask = "run"; visualStudioStep.VisualStudioFile = "App.sln";
    await File.WriteAllTextAsync(Path.Combine(projectRoot, "App.sln"), "test solution");
    Check(Workflow.FromJson(visualStudioWorkflow.ToJson()).Steps[0].VisualStudioFile == "App.sln", "Visual Studio selection and solution survive workflow save/load");
    if (OperatingSystem.IsWindows())
    {
        var vsLog = Path.Combine(scratch, "visual-studio-args.txt");
        visualStudioStep.VisualStudioPath = Path.Combine(AppContext.BaseDirectory, "LaunchForge.Checks.exe");
        Environment.SetEnvironmentVariable("LAUNCHFORGE_VS_TEST_LOG", vsLog);
        async Task<bool> WaitForVisualStudioStub()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(vsLog) && await File.ReadAllTextAsync(vsLog) == Path.Combine(projectRoot, "App.sln")) return true;
                await Task.Delay(100);
            }
            return false;
        }
        try
        {
            Check(await WorkflowRunner.RunAsync(visualStudioWorkflow) == 0 && await WaitForVisualStudioStub(), "Visual Studio handoff preserves the solution path including special characters");
            File.Delete(vsLog);
            await LauncherExporter.ExportAsync(visualStudioWorkflow, ExportFormat.WindowsBatch, launcher);
            Check(await Execute(launcher) == 0 && await WaitForVisualStudioStub(), "Exported batch launches Visual Studio with the selected solution");
        }
        finally { Environment.SetEnvironmentVariable("LAUNCHFORGE_VS_TEST_LOG", null); }
    }
    var javascriptRoot = Path.Combine(scratch, "javascript"); Directory.CreateDirectory(javascriptRoot);
    foreach (var file in new[] { "index.js", "server.mjs", "app.cjs", "vite.config.js" }) await File.WriteAllTextAsync(Path.Combine(javascriptRoot, file), "console.log('example')");
    await File.WriteAllTextAsync(Path.Combine(javascriptRoot, "package.json"), "{\"main\":\"index.js\"}");
    var javascriptProjects = ProjectDetector.Detect(javascriptRoot);
    Check(javascriptProjects.Count == 3 && javascriptProjects.All(project => project.Kind == ProjectKind.JavaScript), "Detects JS, MJS and CJS without npm scripts and skips configuration files");
    var jsStep = new WorkflowStep { Kind = ActionKind.RunProject, ProjectKind = ProjectKind.JavaScript, ProjectFile = "index.js" };
    var openProject = new WorkflowStep { Kind = ActionKind.OpenVsCode, Target = javascriptRoot };
    var linkedWorkflow = Make(openProject, jsStep);
    Check(linkedWorkflow.ProjectFolderFor(jsStep) == javascriptRoot, "Run project inherits the preceding VS Code folder instead of the general folder");
    jsStep.WorkingDirectory = projectRoot;
    Check(linkedWorkflow.ProjectFolderFor(jsStep) == projectRoot, "Explicit run project folder overrides the preceding VS Code folder");
    jsStep.WorkingDirectory = ""; openProject.Target = projectRoot;
    Check(linkedWorkflow.ProjectFolderFor(jsStep) == projectRoot, "Changing the preceding VS Code folder changes the inherited run folder");
    openProject.Target = javascriptRoot;
    Check(jsStep.CommandFor(false, javascriptRoot).Trim() == "'node' 'index.js'", "Standalone JavaScript uses Node.js with the selected entry file");
    var missingFolder = new Workflow { Name = "Missing folder", Steps = [jsStep] };
    try { missingFolder.Validate(OperatingSystem.IsWindows()); Check(false, "Missing project folder rejected"); } catch (InvalidDataException) { Check(true, "Missing project folder rejected"); }
    var custom = Command("echo custom", "printf custom");
    Check(custom.Kind == ActionKind.CustomCommand && custom.CommandFor(true, scratch) == "echo custom", "Existing custom commands remain unchanged");
    var vsTask = new WorkflowStep { Kind = ActionKind.RunProject, Name = "Start my project", ProjectKind = ProjectKind.Node, ProjectTask = "dev", RunIn = RunLocation.VsCode };
    var vsWorkflow = Make(vsTask);
    Check(vsTask.CommandFor(false, "/tmp/project").Contains("vscode://launchforge.launcher/run?request="), "Guided project task routes to VS Code");
    var oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    var toolsFolder = Path.Combine(scratch, "tools"); Directory.CreateDirectory(toolsFolder);
    var toolLog = Path.Combine(scratch, "tool-args.txt");
    var installedMarker = Path.Combine(scratch, "extension-installed.txt");
    if (OperatingSystem.IsWindows())
    {
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "dotnet.cmd"), "@echo off\r\necho %*>>\"%LAUNCHFORGE_TEST_LOG%\"\r\nexit /b 0\r\n");
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "node.cmd"), "@echo off\r\ncd>>\"%LAUNCHFORGE_TEST_LOG%\"\r\necho %*>>\"%LAUNCHFORGE_TEST_LOG%\"\r\nexit /b 0\r\n");
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "code.cmd"), "@echo off\r\nif \"%~1\"==\"--list-extensions\" (\r\n if exist \"%LAUNCHFORGE_TEST_MARKER%\" echo launchforge.launcher\r\n exit /b 0\r\n)\r\nif \"%~1\"==\"--install-extension\" (\r\n if not exist \"%~2\" exit /b 8\r\n echo installed>\"%LAUNCHFORGE_TEST_MARKER%\"\r\n exit /b 0\r\n)\r\necho %*>>\"%LAUNCHFORGE_TEST_LOG%\"\r\nexit /b 0\r\n");
    }
    else
    {
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "dotnet"), $"#!/bin/bash\nprintf '%s\\n' \"$*\" >> {GuidedActions.ShellQuote(toolLog)}\n");
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "node"), $"#!/bin/bash\npwd >> {GuidedActions.ShellQuote(toolLog)}\nprintf '%s\\n' \"$*\" >> {GuidedActions.ShellQuote(toolLog)}\n");
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "code"), $"#!/bin/bash\nif [ \"$1\" = --list-extensions ]; then [ ! -f {GuidedActions.ShellQuote(installedMarker)} ] || echo launchforge.launcher; exit 0; fi\nif [ \"$1\" = --install-extension ]; then test -f \"$2\" || exit 8; touch {GuidedActions.ShellQuote(installedMarker)}; exit 0; fi\nprintf '%s\\n' \"$*\" >> {GuidedActions.ShellQuote(toolLog)}\n");
        foreach (var tool in new[] { "code", "dotnet", "node" }) File.SetUnixFileMode(Path.Combine(toolsFolder, tool), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    try
    {
        Environment.SetEnvironmentVariable("LAUNCHFORGE_TEST_LOG", toolLog);
        Environment.SetEnvironmentVariable("LAUNCHFORGE_TEST_MARKER", installedMarker);
        Environment.SetEnvironmentVariable("PATH", toolsFolder + Path.PathSeparator + oldPath);
        if (OperatingSystem.IsWindows())
        {
            var nodeExecutable = TrustedTools.Resolve("node");
            File.Copy(nodeExecutable, Path.Combine(projectRoot, "powershell.exe"), true);
            File.Copy(nodeExecutable, Path.Combine(projectRoot, "cmd.exe"), true);
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "code.cmd"), "@exit /b 77\r\n");
            var secureGuided = Make(new WorkflowStep { Kind = ActionKind.RunProject, ProjectKind = ProjectKind.DotNet, ProjectTask = "build", ProjectFile = "App.csproj", WorkingDirectory = projectRoot });
            Check(await WorkflowRunner.RunAsync(secureGuided) == 0, "Project-local fake PowerShell and cmd executables cannot hijack guided actions");
            File.Delete(toolLog);
            await LauncherExporter.ExportAsync(secureGuided, format, launcher);
            Check(await Execute(launcher) == 0, "Exported batch ignores project-local system executable impostors");
            File.Delete(toolLog);
        }
        Check(await WorkflowRunner.RunAsync(linkedWorkflow) == 0 && (await File.ReadAllTextAsync(toolLog)).Contains(javascriptRoot), "JavaScript runner executes inside the inherited project folder");
        File.Delete(toolLog);
        await LauncherExporter.ExportAsync(linkedWorkflow, format, launcher);
        Check(await Execute(launcher) == 0 && (await File.ReadAllTextAsync(toolLog)).Contains(javascriptRoot), "Exported JavaScript launcher preserves the inherited folder");
        File.Delete(toolLog);
        var guided = Make(new WorkflowStep { Kind = ActionKind.RunProject, ProjectKind = ProjectKind.DotNet, ProjectTask = "build", ProjectFile = "App.csproj" });
        Check(await WorkflowRunner.RunAsync(guided) == 0 && (await File.ReadAllTextAsync(toolLog)).Contains("build"), "Guided action executes the selected project task without a user command");
        File.Delete(toolLog);
        Check(await WorkflowRunner.RunAsync(vsWorkflow) == 0 && File.Exists(installedMarker), "VS Code integration bootstraps its bundled extension on first use");
        var uriLine = (await File.ReadAllLinesAsync(toolLog)).Single(line => line.Contains("vscode://"));
        var payload = uriLine[(uriLine.IndexOf("request=", StringComparison.Ordinal) + 8)..].Trim().Trim('"');
        payload = payload.Replace('-', '+').Replace('_', '/'); payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var request = JsonDocument.Parse(Convert.FromBase64String(payload));
        Check(request.RootElement.GetProperty("folder").GetString() == scratch && request.RootElement.GetProperty("args")[1].GetString() == "dev", "VS Code handoff preserves folder and task, including special characters");
        File.Delete(installedMarker); File.Delete(toolLog);
        await LauncherExporter.ExportAsync(vsWorkflow, format, launcher);
        Check(File.Exists(Path.Combine(scratch, VsCodeIntegration.PackageName)) && await Execute(launcher) == 0 && File.Exists(installedMarker), "Exported script includes and bootstraps the VS Code integration");
        File.Delete(installedMarker);
        await File.AppendAllTextAsync(Path.Combine(scratch, VsCodeIntegration.PackageName), "tampered");
        Check(await Execute(launcher) != 0 && !File.Exists(installedMarker), "Modified VS Code sidecar is rejected before installation");
    }
    finally { Environment.SetEnvironmentVariable("PATH", oldPath); Environment.SetEnvironmentVariable("LAUNCHFORGE_TEST_LOG", null); Environment.SetEnvironmentVariable("LAUNCHFORGE_TEST_MARKER", null); }
    var large = Make(Enumerable.Range(0, 35).Select(_ => Command("ver >nul", "true")).ToArray());
    await LauncherExporter.ExportAsync(large, format, launcher);
    Check(await Execute(launcher) == 0, "Large exported workflows avoid the Windows command-line limit");
    if (OperatingSystem.IsWindows())
    {
        await LauncherExporter.ExportAsync(exported, ExportFormat.WindowsBatch, launcher);
        var shortcutPath = Path.ChangeExtension(launcher, ".lnk");
        dynamic shortcutShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shortcutShell.CreateShortcut(shortcutPath);
        try
        {
            Check(File.Exists(shortcutPath) && ((string)shortcut.TargetPath).Equals(Path.GetFullPath(launcher), StringComparison.OrdinalIgnoreCase), "Icon shortcut targets the exported batch file");
            Check(((string)shortcut.IconLocation).Contains("success.launchforge.ico") && File.ReadAllBytes(Path.Combine(scratch, "success.launchforge.ico")).SequenceEqual(iconBytes), "Batch shortcut uses a copied, independent icon file");
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shortcutShell); }
    }
    if (args.Contains("--exe"))
    {
        var executable = Path.Combine(scratch, OperatingSystem.IsWindows() ? "launcher.exe" : "launcher");
        await LauncherExporter.ExportAsync(exported, OperatingSystem.IsWindows() ? ExportFormat.WindowsExecutable : ExportFormat.LinuxExecutable, executable, Console.WriteLine);
        if (OperatingSystem.IsWindows()) Check(IconFixture.ReadEmbeddedImage(executable).SequenceEqual(iconBytes.Skip(22)), "Standalone EXE embeds the selected icon in its native resources");
        File.Delete(Path.Combine(scratch, "exported.txt"));
        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })!;
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0 && File.Exists(Path.Combine(scratch, "exported.txt")), "Generated standalone executable runs its embedded workflow");
    }
    Console.WriteLine($"{checks} checks passed.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
finally
{
    // Windows can retain a newly executed file briefly (for example while scanning it).
    // Cleanup must never turn a successful check into an unhandled application error.
    try { Directory.Delete(Path.GetDirectoryName(scratch)!, recursive: true); }
    catch (IOException) { Console.Error.WriteLine("Temporary check files remain at: " + scratch); }
    catch (UnauthorizedAccessException) { Console.Error.WriteLine("Temporary check files remain at: " + scratch); }
}
