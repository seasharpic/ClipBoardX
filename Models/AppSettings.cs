namespace ClipBoardX.Models;

/// <summary>Настройки приложения, хранятся в %APPDATA%\ClipBoardX\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Максимальное количество записей в истории (закреплённые не учитываются).</summary>
    public int MaxItems { get; set; } = 300;

    /// <summary>Сколько дней хранить записи. 0 — не удалять по времени.</summary>
    public int ExpireDays { get; set; } = 14;

    public bool CaptureImages { get; set; } = true;

    public bool CaptureFiles { get; set; } = true;

    public bool CaptureHtml { get; set; } = false;

    public bool RunAtStartup { get; set; }

    public bool StartMinimized { get; set; } = true;

    /// <summary>Не добавлять записи короче указанного числа символов (0 — отключить фильтр).</summary>
    public int MinTextLength { get; set; } = 1;

    /// <summary>
    /// Глобальная горячая клавиша: virtual-key код.
    /// По умолчанию Ctrl+Shift+V — Win+V и Win+Shift+V в Windows заняты
    /// встроенным меню истории буфера обмена (Win+V) и системными сочетаниями.
    /// </summary>
    public int HotkeyVirtualKey { get; set; } = 0x56; // 'V'

    /// <summary>Модификаторы: 1=Alt, 2=Ctrl, 4=Shift, 8=Win.</summary>
    public int HotkeyModifiers { get; set; } = 2 | 4; // Ctrl+Shift+V

    public string HotkeyLabel => BuildHotkeyLabel(HotkeyVirtualKey, HotkeyModifiers);

    public static string BuildHotkeyLabel(int vk, int modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");

        var key = vk switch
        {
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            0x20 => "Space",
            0x0D => "Enter",
            0x1B => "Esc",
            _ => $"0x{vk:X2}"
        };

        parts.Add(key);
        return string.Join("+", parts);
    }
}