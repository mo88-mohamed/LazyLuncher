using System.Diagnostics;

namespace LaunchForge.Core;

public static class WorkflowRunner
{
    public static async Task<int> RunAsync(Workflow workflow, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        bool windows = OperatingSystem.IsWindows();
        workflow.Validate(windows);
        foreach (var step in workflow.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = string.IsNullOrWhiteSpace(step.WorkingDirectory) ? workflow.WorkingDirectory : step.WorkingDirectory;
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder does not exist: {folder}");
            var info = new ProcessStartInfo
            {
                FileName = windows ? "cmd.exe" : "/bin/bash",
                WorkingDirectory = folder,
                UseShellExecute = false
            };
            if (windows) info.Arguments = "/d /s /c \"" + step.WindowsCommand + "\"";
            else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(step.LinuxCommand); }
            log?.Invoke($"Starting: {step.Name}");
            using var process = Process.Start(info) ?? throw new IOException($"Could not start '{step.Name}'.");
            if (!step.WaitForExit) continue;
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                throw;
            }
            if (process.ExitCode != 0)
            {
                log?.Invoke($"Stopped: {step.Name} returned {process.ExitCode}.");
                return process.ExitCode;
            }
        }
        log?.Invoke("All actions started successfully.");
        return 0;
    }
}
