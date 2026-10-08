namespace ClipBoardX.Services;

/// <summary>Пути хранения данных приложения.</summary>
internal static class AppPaths
{
    private static readonly Lazy<string> LazyDataDir =
        new(() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipBoardX"));

    public static string DataDir => LazyDataDir.Value;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string HistoryFile => Path.Combine(DataDir, "history.json");

    public static string ImagesDir => Path.Combine(DataDir, "images");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
    }
}