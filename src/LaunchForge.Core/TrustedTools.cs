namespace LaunchForge.Core;

public static class TrustedTools
{
    public static string CommandShell => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/bash";

    public static string Resolve(string name)
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory) || Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            foreach (var suffix in OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", ".com" } : new[] { "" })
            {
                var candidate = Path.Combine(directory, name + suffix);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException($"Install {name} and add its installation folder to PATH.");
    }

    public const string WindowsPrelude = """
        function Resolve-LaunchForgeTool([string]$name) {
          foreach ($entry in ($env:PATH -split ';')) {
            $directory = $entry.Trim('"')
            if (-not [IO.Path]::IsPathRooted($directory) -or $directory -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)' -or [IO.Path]::GetFullPath($directory).TrimEnd('\','/') -eq $PWD.Path.TrimEnd('\','/')) { continue }
            $extensions = if ([IO.Path]::GetExtension($name)) { @('') } else { @('.exe','.cmd','.bat','.com') }
            foreach ($extension in $extensions) {
              $candidate = Join-Path $directory ($name + $extension)
              if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
            }
          }
          return $null
        }
        """;
}
