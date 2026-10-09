using System.Diagnostics;
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
        var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash", UseShellExecute = false };
        if (OperatingSystem.IsWindows()) info.Arguments = "/d /s /c \"\"" + path + "\"\"";
        else info.ArgumentList.Add(path);
        using var process = Process.Start(info)!; await process.WaitForExitAsync(); return process.ExitCode;
    }
    Check(await Execute(launcher) == 0 && File.Exists(Path.Combine(scratch, "exported.txt")), "Exported script runs with spaces, apostrophe, ampersand and percent in path");
    await LauncherExporter.ExportAsync(fail, format, launcher);
    Check(await Execute(launcher) == 7 && !File.Exists(Path.Combine(scratch, "should-not-exist.txt")), "Exported script stops on failure");
    var cancellation = Make(Command("ping -n 30 127.0.0.1 >nul", "sleep 30"), Command("echo bad>cancelled.txt", "touch cancelled.txt"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    try { await WorkflowRunner.RunAsync(cancellation, cancellationToken: timeout.Token); Check(false, "Cancellation stops the workflow"); }
    catch (OperationCanceledException) { Check(!File.Exists(Path.Combine(scratch, "cancelled.txt")), "Cancellation stops the workflow"); }
    if (args.Contains("--exe"))
    {
        var executable = Path.Combine(scratch, OperatingSystem.IsWindows() ? "launcher.exe" : "launcher");
        await LauncherExporter.ExportAsync(exported, OperatingSystem.IsWindows() ? ExportFormat.WindowsExecutable : ExportFormat.LinuxExecutable, executable, Console.WriteLine);
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
