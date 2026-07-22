namespace ClaudeUsage;

public static class StartupShortcut
{
    private static string LnkPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ClaudeUsage.lnk");

    public static bool Exists() => File.Exists(LnkPath);

    public static bool TryCreate()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(LnkPath);
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            shortcut.Description = "Overlay des limites d'usage";
            shortcut.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Remove()
    {
        try
        {
            File.Delete(LnkPath);
        }
        catch
        {
        }
    }
}
