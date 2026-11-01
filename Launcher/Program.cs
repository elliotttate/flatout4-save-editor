using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FlatOut4SaveEditorLauncher;

// FlatOut4SaveEditor-v<version>-Setup.exe, the optional per-user install (the portable ZIP is the
// alternative that installs nothing). It extracts the app to %LOCALAPPDATA%\FlatOut4SaveEditor\app,
// re-extracting only when this setup's version differs, adds a "FlatOut 4 Save Editor" Start menu
// shortcut and starts the app. No administrator rights are needed.
internal static class Program
{
    private const string ResourceName = "FlatOut4SaveEditor.zip";
    private const string AppFolderName = "FlatOut4SaveEditor";
    private const string AppExeName = "FlatOut4SaveEditor.exe";
    private const string ShortcutName = "FlatOut 4 Save Editor.lnk";
    private const string Caption = "FlatOut 4 Save Editor Setup";

    // Tests point this at a scratch folder so they never touch the real install or Start menu.
    private const string RootOverrideVariable = "FLATOUT4SAVEEDITOR_SETUP_ROOT";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string? overrideRoot = Environment.GetEnvironmentVariable(RootOverrideVariable);
            string root = string.IsNullOrWhiteSpace(overrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName)
                : overrideRoot;
            string startMenu = string.IsNullOrWhiteSpace(overrideRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                : Path.Combine(root, "Start Menu");
            string appDir = Path.Combine(root, "app");
            string appExe = Path.Combine(appDir, AppExeName);
            string versionMarker = Path.Combine(appDir, ".launcher-version");

            Directory.CreateDirectory(root);

            string thisStamp = GetBuildStamp();
            bool needExtract = !File.Exists(appExe);
            if (!needExtract && File.Exists(versionMarker))
            {
                try
                {
                    needExtract = File.ReadAllText(versionMarker).Trim() != thisStamp;
                }
                catch
                {
                    needExtract = true;
                }
            }
            else
            {
                needExtract = true;
            }

            if (needExtract)
            {
                if (IsRunning(appExe))
                {
                    return Fail(
                        "FlatOut 4 Save Editor is open from its installed copy, so Setup can't update it.\n\n" +
                        "Close the editor and run Setup again.",
                        logDetails: false);
                }

                ExtractApp(appDir);
                File.WriteAllText(versionMarker, thisStamp);
            }

            if (!File.Exists(appExe))
            {
                return Fail($"Setup ran but the expected app at {appExe} was not produced.");
            }

            // Best effort: the editor still starts if the shortcut can't be written.
            try
            {
                CreateShortcut(Path.Combine(startMenu, ShortcutName), appExe, appDir);
            }
            catch (Exception ex)
            {
                Log($"Start menu shortcut not created: {ex.GetType().Name}: {ex.Message}");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = appExe,
                WorkingDirectory = appDir,
                UseShellExecute = true,
                Arguments = string.Join(" ", args.Select(QuoteArgument))
            });

            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"Setup failed: {ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
        }
    }

    private static bool IsRunning(string appExe)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appExe)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, appExe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Another user's process, or one that exited meanwhile.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static void ExtractApp(string appDir)
    {
        if (Directory.Exists(appDir))
        {
            Directory.Delete(appDir, true);
        }

        Directory.CreateDirectory(appDir);
        string appDirFullPath = Path.GetFullPath(appDir);
        if (!appDirFullPath.EndsWith(Path.DirectorySeparatorChar))
        {
            appDirFullPath += Path.DirectorySeparatorChar;
        }

        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded archive '{ResourceName}' is missing from the setup binary.");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(
                appDir,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

            if (!destinationPath.StartsWith(appDirFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry escapes the app folder: {entry.FullName}");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using Stream source = entry.Open();
            using FileStream destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }
    }

    // Rewritten on every run, so it always points at the current install.
    private static void CreateShortcut(string shortcutPath, string target, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription("Edit FlatOut 4 and FlatOut VR save files");
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string GetBuildStamp()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0";
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ') || argument.Contains('"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

    private static void Log(string message)
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "setup-error.log"), $"{DateTime.UtcNow:O}\n{message}\n");
        }
        catch
        {
        }
    }

    private static int Fail(string message, bool logDetails = true)
    {
        if (logDetails)
        {
            Log(message);
            message += "\n\nDetails saved to %LOCALAPPDATA%\\" + AppFolderName + "\\setup-error.log";
        }

        MessageBoxW(IntPtr.Zero, message, Caption, 0x00000010);
        return 1;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    // Declared in vtable order; only the setters are used.
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
