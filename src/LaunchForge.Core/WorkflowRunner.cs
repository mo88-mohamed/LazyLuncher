using System.Diagnostics;

namespace LaunchForge.Core;

public static class WorkflowRunner
{
    public static async Task<int> RunAsync(Workflow workflow, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        bool windows = OperatingSystem.IsWindows();
        workflow.Validate(windows);
        bool integrationReady = false;
        foreach (var step in workflow.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectFolder = workflow.ProjectFolderFor(step);
            var folder = step.ExecutionFolder(projectFolder);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder does not exist: {folder}");
            if (step.UsesVsCodeIntegration && !integrationReady)
            {
                await VsCodeIntegration.EnsureInstalledAsync(log, cancellationToken);
                integrationReady = true;
            }
            var info = new ProcessStartInfo
            {
                FileName = windows ? "cmd.exe" : "/bin/bash",
                WorkingDirectory = folder,
                UseShellExecute = false
            };
            var command = step.CommandFor(windows, projectFolder);
            if (windows) info.Arguments = "/d /s /c \"" + command + "\"";
            else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(command); }
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
