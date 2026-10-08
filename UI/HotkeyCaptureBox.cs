using ClipBoardX.Models;

namespace ClipBoardX.UI;

/// <summary>
/// Поле, в которое пользователь «набирает» сочетание клавиш:
/// нажал и отпустил — сочетание сохранено.
/// </summary>
internal sealed class HotkeyCaptureBox : TextBox
{
    private const int RequiredModifiers = 1;

    private int _modifiers = 2 | 4;
    private int _virtualKey = 0x56;

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        TextAlign = HorizontalAlignment.Left;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
    }

    public int Modifiers => _modifiers;

    public int VirtualKey => _virtualKey;

    public void SetHotkey(int modifiers, int virtualKey)
    {
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        UpdateText();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        UpdateText();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateText();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.ControlKey or Keys.LWin or Keys.RWin or Keys.Menu or Keys.ShiftKey
            || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var modifiers = 0;
        if (e.Control) modifiers |= 2;
        if (e.Alt) modifiers |= 1;
        if (e.Shift) modifiers |= 4;
        if (IsWinDown(e.KeyData)) modifiers |= 8;

        var key = NormalizeVirtualKey(e.KeyCode);

        if (key == 0)
        {
            // Нажат только модификатор — ждём основную клавишу.
            e.SuppressKeyPress = true;
            return;
        }

        if (CountBits(modifiers) < RequiredModifiers)
        {
            Text = "нужен хотя бы один модификатор (Ctrl / Alt / Shift / Win)";
            e.SuppressKeyPress = true;
            return;
        }

        _modifiers = modifiers;
        _virtualKey = key;

        UpdateText();
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private static bool IsWinDown(Keys keyData) =>
        (keyData & Keys.Modifiers) is Keys.LWin or Keys.RWin;

    /// <summary>Отбрасывает коды модификаторов: для них нет отдельной «буквы».</summary>
    private static int NormalizeVirtualKey(Keys key)
    {
        var code = (int)key;
        if (code is >= 0x10 and <= 0x12 or 0x5B or 0x5C or 0x5D or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B)
        {
            return 0;
        }

        if (key == Keys.Menu) return 0;
        if (key == Keys.ShiftKey) return 0;
        if (key == Keys.ControlKey) return 0;

        return code;
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private void UpdateText()
    {
        Text = Focused
            ? "нажмите сочетание…"
            : AppSettings.BuildHotkeyLabel(_virtualKey, _modifiers);
    }
}