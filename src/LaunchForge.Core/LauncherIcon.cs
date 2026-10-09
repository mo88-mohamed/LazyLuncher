using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LaunchForge.Core;

public static class LauncherIcon
{
    public static void Validate(string path)
    {
        if (!Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a Windows icon file (.ico).");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected icon was moved or deleted. Choose it again.", path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 22 || stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("The selected icon file is invalid or too large.");
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) throw new InvalidDataException("The selected file is not a valid .ico icon.");
        int images = reader.ReadUInt16();
        long headerSize = 6 + images * 16L;
        if (images == 0 || headerSize > stream.Length) throw new InvalidDataException("The icon file contains no valid images.");
        for (int image = 0; image < images; image++)
        {
            stream.Position = 6 + image * 16L + 8;
            uint length = reader.ReadUInt32(); uint offset = reader.ReadUInt32();
            if (length == 0 || offset < headerSize || (long)offset + length > stream.Length) throw new InvalidDataException("The icon file is incomplete.");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void CreateBatchShortcut(string batchPath, string iconPath, string description)
    {
        var batch = Path.GetFullPath(batchPath);
        var icon = Path.Combine(Path.GetDirectoryName(batch)!, Path.GetFileNameWithoutExtension(batch) + ".launchforge.ico");
        if (!Path.GetFullPath(iconPath).Equals(icon, StringComparison.OrdinalIgnoreCase)) File.Copy(iconPath, icon, overwrite: true);
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable."));
            dynamic shell = shellObject!;
            shortcutObject = shell.CreateShortcut(Path.ChangeExtension(batch, ".lnk"));
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = batch;
            shortcut.WorkingDirectory = Path.GetDirectoryName(batch);
            shortcut.IconLocation = icon + ",0";
            shortcut.Description = description;
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null) Marshal.FinalReleaseComObject(shortcutObject);
            if (shellObject is not null) Marshal.FinalReleaseComObject(shellObject);
        }
    }
}
