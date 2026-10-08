using System.Text.Json.Serialization;

namespace ClipBoardX.Models;

/// <summary>Тип содержимого, которое лежит в буфере обмена.</summary>
public enum ClipKind
{
    Text,
    Html,
    Image,
    Files
}

/// <summary>Одна запись истории буфера обмена.</summary>
public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public ClipKind Kind { get; set; } = ClipKind.Text;

    /// <summary>Текстовое содержимое (для Text/Html — исходный текст, для Files — не используется).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Список путей файлов, если Kind == Files.</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>Путь к сохранённому файлу изображения, если Kind == Image.</summary>
    public string? ImagePath { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool Pinned { get; set; }

    /// <summary>Сколько раз запись копировалась обратно в буфер.</summary>
    public int UseCount { get; set; }

    /// <summary>Хэш содержимого для дедупликации.</summary>
    public string Hash { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

    [JsonIgnore]
    public string Preview => BuildPreview();

    [JsonIgnore]
    public string TimeLabel => CreatedLocal.ToString("HH:mm:ss");

    [JsonIgnore]
    public string DayLabel => CreatedLocal.ToString("dd.MM.yyyy");

    private string BuildPreview()
    {
        var body = Kind switch
        {
            ClipKind.Image => "🖼 Изображение",
            ClipKind.Files => Files.Count == 1
                ? $"📁 {Path.GetFileName(Files[0])}"
                : $"📁 Файлов: {Files.Count}",
            ClipKind.Html => "🌐 HTML",
            _ => Text
        };

        var single = body.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        const int limit = 160;
        return single.Length <= limit ? single : single[..limit] + "…";
    }
}