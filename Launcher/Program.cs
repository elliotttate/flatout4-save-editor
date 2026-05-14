using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FlatOut4SaveEditorLauncher;

internal static class Program
{
    private const string ResourceName = "FlatOut4SaveEditor.zip";
    private const string AppFolderName = "FlatOut4SaveEditor";
    private const string AppExeName = "FlatOut4SaveEditor.exe";
    private const string Caption = "FlatOut 4 Save Editor Setup";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
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
                ExtractApp(appDir);
                File.WriteAllText(versionMarker, thisStamp);
            }

            if (!File.Exists(appExe))
            {
                return Fail($"Setup ran but the expected app at {appExe} was not produced.");
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

    private static int Fail(string message)
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

        MessageBoxW(
            IntPtr.Zero,
            message + "\n\nDetails saved to %LOCALAPPDATA%\\" + AppFolderName + "\\setup-error.log",
            Caption,
            0x00000010);
        return 1;
    }
}
