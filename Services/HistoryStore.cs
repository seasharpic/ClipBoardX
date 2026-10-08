using System.Text.Json;
using ClipBoardX.Models;

namespace ClipBoardX.Services;

/// <summary>Хранилище истории в JSON-файле в %APPDATA%\ClipBoardX.</summary>
internal sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public List<ClipItem> Items { get; } = new();

    public void Load(AppSettings settings)
    {
        if (!File.Exists(AppPaths.HistoryFile)) return;

        try
        {
            var json = File.ReadAllText(AppPaths.HistoryFile);
            var loaded = JsonSerializer.Deserialize<List<ClipItem>>(json, JsonOptions);
            if (loaded is null) return;

            Items.Clear();
            Items.AddRange(loaded.Where(i => i is not null));

            // Чистим протухшие записи и осиротевшие картинки.
            if (settings.ExpireDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-settings.ExpireDays);
                var expired = Items.Where(i => !i.Pinned && i.CreatedUtc < cutoff).ToList();
                foreach (var item in expired) RemoveItem(item, deleteImageFile: true);
            }

            CleanupImages();
            Trim(settings);
        }
        catch (Exception)
        {
            // Повреждённый файл истории не должен мешать запуску.
            Items.Clear();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            var tmp = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items, JsonOptions));
            File.Move(tmp, AppPaths.HistoryFile, overwrite: true);
        }
        catch (IOException)
        {
            // Недоступный диск — молча пропускаем сохранение.
        }
    }

    public void Add(ClipItem item, AppSettings settings)
    {
        // Дубликат: переносим существующую запись вверх и обновляем время.
        var existing = Items.FirstOrDefault(i =>
            i.Hash.Length > 0 &&
            string.Equals(i.Hash, item.Hash, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (!existing.Pinned)
            {
                Items.Remove(existing);
                existing.CreatedUtc = DateTime.UtcNow;
                Items.Insert(0, existing);
            }
            return;
        }

        Items.Insert(0, item);
        Trim(settings);
    }

    public void Trim(AppSettings settings)
    {
        if (settings.MaxItems <= 0) return;

        var unpinned = Items.Where(i => !i.Pinned).ToList();
        if (unpinned.Count <= settings.MaxItems) return;

        var toRemove = unpinned.Skip(settings.MaxItems).ToList();
        foreach (var item in toRemove)
        {
            RemoveItem(item, deleteImageFile: true);
        }
    }

    public void RemoveItem(ClipItem item, bool deleteImageFile)
    {
        Items.Remove(item);

        if (deleteImageFile && item.Kind == ClipKind.Image && !string.IsNullOrEmpty(item.ImagePath))
        {
            TryDeleteFile(item.ImagePath);
        }
    }

    public void Clear(bool includePinned)
    {
        var toRemove = includePinned ? Items.ToList() : Items.Where(i => !i.Pinned).ToList();
        foreach (var item in toRemove)
        {
            RemoveItem(item, deleteImageFile: true);
        }
    }

    /// <summary>Удаляет с диска картинки, на которые больше нет ссылок в истории.</summary>
    public void CleanupImages()
    {
        var referenced = Items
            .Where(i => i.Kind == ClipKind.Image && !string.IsNullOrEmpty(i.ImagePath))
            .Select(i => i.ImagePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(AppPaths.ImagesDir)) return;

        foreach (var file in Directory.EnumerateFiles(AppPaths.ImagesDir, "*.png"))
        {
            if (!referenced.Contains(file))
            {
                TryDeleteFile(file);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}