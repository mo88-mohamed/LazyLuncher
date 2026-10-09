# LaunchForge

A local .NET desktop app for Windows and Linux. Configure ordered commands and export a launcher to run them with one click.

Example: open a project in VS Code, then run `dotnet run` or `npm run dev` in that project's folder.

## Run the desktop app

Install the .NET 10 SDK, then run from this repository:

```sh
dotnet restore LaunchForge.slnx
dotnet run --project src/LaunchForge.Desktop
```

On Windows you can also double-click `start-windows.bat`. On Linux use `bash start-linux.sh`.

The desktop interface uses Avalonia 11. On Linux it needs a graphical desktop and Avalonia's native desktop dependencies: https://docs.avaloniaui.net/docs/platform-specific-guides/linux/

1. Use the VS Code example, or create a new workflow.
2. Enter the project's full working folder path.
3. Add actions in order. Enter Windows commands for Windows launchers, Linux commands for Linux launchers, or both.
4. Leave **Wait for completion** enabled to run actions sequentially and stop on a nonzero exit code. Disable it for an action that should keep running while later actions start.
5. Save the workflow as JSON, and choose **Export launcher**.

`code .` normally returns after handing the project to VS Code, so the next action can start while the editor remains open. Run commands execute in the project folder, independently of VS Code's integrated terminal. The `code` command must be installed on PATH. Use `code --wait .` only if you intend to wait until VS Code closes.

## Export formats

| Format | Runs on | Requirements |
| --- | --- | --- |
| `.bat` | Windows | Built-in Windows PowerShell 5.1 or later; no .NET needed |
| `.sh` | Linux | Bash; `chmod +x launcher.sh` when transferred from Windows |
| `.exe` | Windows x64 | Self-contained .NET runtime bundled in the executable |
| Native binary | Linux x64 | Self-contained .NET runtime bundled; OS native dependencies still apply |

Executable generation needs the **.NET 10 SDK on the builder computer**, with NuGet access for the first runtime download. The person running the generated launcher does not need the SDK or desktop app. Executables contain the workflow; the JSON does not need to be distributed separately. The runtime makes standalone launchers larger than scripts.

Batch files encode their commands in PowerShell to preserve special characters in paths. Very large workflows exceed Windows' command-line limit and must be exported as executables instead. Linux files use LF endings and shell quoting.

Commands run with the current user's permissions. Review imported workflows before running them. Project files, VS Code, and command-line tools are not packaged into a launcher and must exist on the target computer. Use target-computer folder paths when exporting for another OS. Keep credentials out of workflows and exported launchers.

## Build and verify

```sh
dotnet build LaunchForge.slnx -c Release
dotnet run --project tests/LaunchForge.Checks -c Release
# Also generate and execute a self-contained launcher for the current OS:
dotnet run --project tests/LaunchForge.Checks -c Release -- --exe
```

Checks exercise command ordering, failure handling, cancellation, JSON round trips, and actual exported launcher execution with special characters in folder names. Run on both Windows and Linux to verify each platform. CI runs the normal checks on both.

Publish the desktop app for a target OS:

```sh
dotnet publish src/LaunchForge.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/windows
dotnet publish src/LaunchForge.Desktop -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/linux
```

## Repository layout

- `src/LaunchForge.Core`: workflow model, command runner, script and executable exporters.
- `src/LaunchForge.Desktop`: desktop workflow editor and file pickers.
- `tests/LaunchForge.Checks`: behavioral checks without test-framework dependencies.
- `examples/vscode-dotnet.json`: editable example; replace the project path.

This is an initial version. Exports target x64. It does not package entire projects, elevate privileges, create installers, or control the VS Code integrated terminal. Edits are saved manually with **Save workflow**; loading or creating a workflow replaces unsaved edits. Cancellation stops the currently awaited process tree; actions explicitly launched without waiting continue independently.
