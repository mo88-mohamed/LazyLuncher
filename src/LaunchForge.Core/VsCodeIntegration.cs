using System.Diagnostics;
using System.Security.Cryptography;

namespace LaunchForge.Core;

public static class VsCodeIntegration
{
    public const string ExtensionId = "launchforge.launcher";
    public const string PackageName = "LaunchForge.VSCode.vsix";
    public static string PackageHash
    {
        get
        {
            using var source = typeof(VsCodeIntegration).Assembly.GetManifestResourceStream(PackageName)!;
            return Convert.ToHexString(SHA256.HashData(source));
        }
    }

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
        log?.Invoke("Setting up Lazy Launcher's VS Code terminal integration...");
        var directory = Path.Combine(Path.GetTempPath(), "LaunchForge", "vscode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var package = Path.Combine(directory, PackageName);
            await WritePackageAsync(package, token);
            var command = GuidedActions.Tool("code", ["--install-extension", package], OperatingSystem.IsWindows());
            var result = await RunShellAsync(command, token);
            if (result.ExitCode != 0) throw new IOException("Could not install the VS Code integration.\n" + result.Output);
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static Task<(int ExitCode, string Output)> RunCodeAsync(string arguments, CancellationToken token) => RunShellAsync(GuidedActions.Tool("code", [arguments], OperatingSystem.IsWindows()), token);

    private static async Task<(int ExitCode, string Output)> RunShellAsync(string command, CancellationToken token)
    {
        var info = new ProcessStartInfo { FileName = TrustedTools.CommandShell, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (OperatingSystem.IsWindows()) info.Arguments = "/d /s /c \"" + command + "\"";
        else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(command); }
        using var process = Process.Start(info) ?? throw new IOException("Could not start VS Code.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    public static string WindowsBootstrap => $$"""
        $codeTool = Resolve-LaunchForgeTool 'code'
        if (-not $codeTool) { throw 'Install VS Code and add its installation folder to PATH.' }
        $installed = @(& $codeTool --list-extensions)
        if ($LASTEXITCODE -ne 0) { throw 'Install VS Code and enable the code command on PATH.' }
        if ($installed -notcontains 'launchforge.launcher') {
            $packageDirectory = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $packageDirectory | Out-Null
            try {
                $package = Join-Path $packageDirectory 'LaunchForge.VSCode.vsix'
                Copy-Item -LiteralPath (Join-Path $env:LAUNCHFORGE_LAUNCHER_DIR 'LaunchForge.VSCode.vsix') -Destination $package
                if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '{{PackageHash}}') { throw 'VS Code integration was modified. Export the launcher again.' }
                & $codeTool --install-extension $package
                if ($LASTEXITCODE -ne 0) { throw 'Could not install the Lazy Launcher VS Code integration.' }
            } finally { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
        }
        """;

    public static string LinuxBootstrap => $$"""
        command -v code >/dev/null || { printf '%s\n' 'Install VS Code and enable the code command on PATH.' >&2; exit 1; }
        if ! code --list-extensions | grep -Fxiq 'launchforge.launcher'; then
          launcher_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
          package_dir="$(mktemp -d)"
          if ! cp -- "$launcher_dir/LaunchForge.VSCode.vsix" "$package_dir/integration.vsix"; then rm -rf -- "$package_dir"; exit 1; fi
          expected='{{PackageHash.ToLowerInvariant()}}'
          actual="$(sha256sum -- "$package_dir/integration.vsix")"
          if [ "${actual%% *}" != "$expected" ]; then rm -rf -- "$package_dir"; printf '%s\n' 'VS Code integration was modified. Export the launcher again.' >&2; exit 1; fi
          if ! code --install-extension "$package_dir/integration.vsix"; then rm -rf -- "$package_dir"; exit 1; fi
          rm -rf -- "$package_dir"
        fi
        """;
}
