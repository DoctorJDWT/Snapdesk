namespace LayoutProfiles.WinUI.Services;

public static class RepoPaths
{
    private static string? _cachedRoot;

    public static string RepoRoot
    {
        get
        {
            if (_cachedRoot is not null)
            {
                return _cachedRoot;
            }

            // Dev repo root and installed layout (%LOCALAPPDATA%\Programs\Snapdesk\) both
            // contain invoke_python.ps1 plus src\layout_manager.py next to the published exe.
            var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (IsAppRoot(dir))
                {
                    _cachedRoot = dir;
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            // Installed shortcut may start the exe directly; probe the standard install dir.
            var defaultInstall = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Snapdesk");
            if (IsAppRoot(defaultInstall))
            {
                _cachedRoot = defaultInstall;
                return defaultInstall;
            }

            _cachedRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return _cachedRoot;
        }
    }

    private static bool IsAppRoot(string dir) =>
        File.Exists(Path.Combine(dir, "invoke_python.ps1"))
        && (File.Exists(Path.Combine(dir, "layout_manager.py"))
            || File.Exists(Path.Combine(dir, "src", "layout_manager.py")));

    public static string LayoutProfilesDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayoutProfiles");

    public static string ProfilesDir => Path.Combine(LayoutProfilesDataDir, "profiles");

    public static string SettingsPath => Path.Combine(LayoutProfilesDataDir, "settings.json");
}
