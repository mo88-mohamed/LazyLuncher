using System.Diagnostics;

namespace LaunchForge.Core;

public static class VsCodeIntegration
{
    public const string ExtensionId = "launchforge.launcher";
    public const string PackageName = "LaunchForge.VSCode.vsix";

    public static async Task WritePackageAsync(string destination, CancellationToken token = default)
    {
        await using var source = typeof(VsCodeIntegration).Assembly.GetManifestResourceStream(PackageName)
            ?? throw new IOException("The VS Code integration package is missing.");
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, token);
    }

    public static async Task EnsureInstalledAsync(Action<string>? log = null, CancellationToken token = default)
    {
        var installed = await RunCodeAsync("--list-extensions", token);
        if (installed.ExitCode != 0) throw new IOException("VS Code could not be found. Install VS Code and enable the 'code' command on PATH.\n" + installed.Output);
        if (installed.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(line => line.Trim().Equals(ExtensionId, StringComparison.OrdinalIgnoreCase))) return;
        log?.Invoke("Setting up LaunchForge's VS Code terminal integration...");
        var directory = Path.Combine(Path.GetTempPath(), "LaunchForge", "vscode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var package = Path.Combine(directory, PackageName);
            await WritePackageAsync(package, token);
            var command = OperatingSystem.IsWindows()
                ? GuidedActions.WindowsScript($"& code --install-extension {GuidedActions.PowerShellQuote(package)}; exit $LASTEXITCODE")
                : "code --install-extension " + GuidedActions.ShellQuote(package);
            var result = await RunShellAsync(command, token);
            if (result.ExitCode != 0) throw new IOException("Could not install the VS Code integration.\n" + result.Output);
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static Task<(int ExitCode, string Output)> RunCodeAsync(string arguments, CancellationToken token) => RunShellAsync("code " + arguments, token);

    private static async Task<(int ExitCode, string Output)> RunShellAsync(string command, CancellationToken token)
    {
        var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (OperatingSystem.IsWindows()) info.Arguments = "/d /s /c \"" + command + "\"";
        else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(command); }
        using var process = Process.Start(info) ?? throw new IOException("Could not start VS Code.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    public static string WindowsBootstrap => """
        $installed = @(& code --list-extensions)
        if ($LASTEXITCODE -ne 0) { throw 'Install VS Code and enable the code command on PATH.' }
        if ($installed -notcontains 'launchforge.launcher') {
            & code --install-extension (Join-Path $env:LAUNCHFORGE_LAUNCHER_DIR 'LaunchForge.VSCode.vsix')
            if ($LASTEXITCODE -ne 0) { throw 'Could not install the LaunchForge VS Code integration.' }
        }
        """;

    public static string LinuxBootstrap => """
        command -v code >/dev/null || { printf '%s\n' 'Install VS Code and enable the code command on PATH.' >&2; exit 1; }
        if ! code --list-extensions | grep -Fxiq 'launchforge.launcher'; then
          launcher_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
          code --install-extension "$launcher_dir/LaunchForge.VSCode.vsix"
        fi
        """;
}
