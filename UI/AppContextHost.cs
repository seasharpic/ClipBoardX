using System.Runtime.InteropServices;
using ClipBoardX.Models;
using ClipBoardX.Services;

namespace ClipBoardX.UI;

/// <summary>
/// Основной контекст приложения: трей, глобальная горячая клавиша,
/// слежение за буфером и сохранение истории.
/// </summary>
internal sealed class AppContextHost : ApplicationContext
{
    private readonly HistoryStore _store = new();
    private readonly SettingsService _settings = new();
    private readonly NotifyIcon _tray;
    private readonly ClipboardListenerWindow _listener;
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 2000 };
    private readonly HistoryForm _form;

    private bool _hotkeyRegistered;
    private bool _dirty;

    public bool ExitRequested { get; set; }

    /// <summary>Окно, которое было активно до открытия истории — цель для вставки.</summary>
    public IntPtr LastForegroundWindow { get; private set; } = IntPtr.Zero;

    public AppContextHost(bool startMinimized)
    {
        AppPaths.EnsureCreated();

        _settings.Load();
        _store.Load(_settings.Current);

        _form = new HistoryForm(_store, _settings, this);

        // Формируем дескриптор окна сразу: иначе второй экземпляр не сможет
        // попросить показать историю, пока окно ни разу не открывали.
        _ = _form.Handle;

        _listener = new ClipboardListenerWindow();
        _listener.ClipboardUpdated += OnClipboardUpdated;
        _listener.HotkeyReceived += _ => ToggleWindow();

        _tray = BuildTrayIcon();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (!_dirty) return;
            _dirty = false;
            _store.Save();
        };

        RegisterHotKey(_settings.Current);

        // При запуске с --autostart окно не показываем.
        if (!startMinimized)
        {
            ShowWindow();
        }
    }

    public AppSettings Settings => _settings.Current;

    public void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ── Буфер обмена ───────────────────────────────────────────────────
    private void OnClipboardUpdated()
    {
        // Наш собственный ввод в буфер не должен попадать в историю дважды.
        if (ClipboardReader.IsInternalWrite) return;

        try
        {
            var item = ClipboardReader.Capture(_settings.Current);
            if (item is null) return;

            var before = _store.Items.Count;
            _store.Add(item, _settings.Current);

            if (_store.Items.Count != before || item.UseCount > 0)
            {
                MarkDirty();
            }

            UpdateTrayText();
        }
        catch (Exception)
        {
            // Сбой чтения буфера не должен ронять приложение.
        }
    }

    // ── Окно ───────────────────────────────────────────────────────────

    /// <summary>Показывает окно истории (вызывается из потока сигнала запуска).</summary>
    public void RequestShowWindow()
    {
        var form = _form;

        if (form is null || !form.IsHandleCreated)
        {
            return;
        }

        if (form.InvokeRequired)
        {
            form.BeginInvoke(ShowWindow);
        }
        else
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _listener.Handle)
        {
            LastForegroundWindow = foreground;
        }

        _form.RefreshListPublic();
        _form.ShowNearCursor();
    }

    private void ToggleWindow()
    {
        if (_form.Visible && _form.ContainsFocus)
        {
            _form.Hide();
        }
        else
        {
            ShowWindow();
        }
    }

    public void ShowSettings()
    {
        using var dialog = new SettingsForm(_settings, this);
        dialog.ShowDialog(_form);
    }

    // ── Трей ───────────────────────────────────────────────────────────
    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(45, 47, 53),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F),
            ShowImageMargin = false
        };

        var openItem = new ToolStripMenuItem($"Открыть историю\t{_settings.Current.HotkeyLabel}", null, (_, _) => ShowWindow());
        openItem.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        var pinnedItem = new ToolStripMenuItem("Закреплённые", null, (_, _) =>
        {
            _form.OpenFilter(5);
            ShowWindow();
        });

        menu.Items.Add(openItem);
        menu.Items.Add(pinnedItem);
        menu.Items.Add(new ToolStripSeparator());

        var runAtStart = new ToolStripMenuItem("Запускать вместе с Windows")
        {
            Checked = _settings.Current.RunAtStartup,
            CheckOnClick = true
        };
        runAtStart.CheckedChanged += (_, _) =>
        {
            _settings.Current.RunAtStartup = runAtStart.Checked;
            _settings.ApplyRunAtStartup(runAtStart.Checked);
            _settings.Save();
        };

        menu.Items.Add(runAtStart);
        menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());

        var clearItem = new ToolStripMenuItem("Очистить историю", null, (_, _) => ClearHistoryFromTray());
        menu.Items.Add(clearItem);
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitApp()));

        var icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTrayText(),
            ContextMenuStrip = menu,
            Visible = true
        };

        icon.DoubleClick += (_, _) => ShowWindow();
        return icon;
    }

    private void ClearHistoryFromTray()
    {
        var result = MessageBox.Show(
            "Удалить все незакреплённые записи?",
            "ClipBoardX",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (result != DialogResult.OK) return;

        _store.Clear(includePinned: false);
        MarkDirty();
        UpdateTrayText();

        if (_form.Visible) _form.RefreshListPublic();
    }

    private string BuildTrayText() =>
        _store.Items.Count > 0 ? $"ClipBoardX — записей: {_store.Items.Count}" : "ClipBoardX";

    private void UpdateTrayText() => _tray.Text = BuildTrayText();

    // ── Горячая клавиша ────────────────────────────────────────────────

    /// <summary>
    /// Резервные сочетания, если основное занято. Win+V и Win+Shift+V в Windows
    /// не доступны — они заняты встроенным меню истории буфера обмена.
    /// </summary>
    private static readonly (int Modifiers, int Key)[] FallbackHotKeys =
    {
        (2 | 4, 0x56), // Ctrl+Shift+V
        (2 | 4, 0x58), // Ctrl+Shift+X
        (1 | 2 | 8, 0x56) // Ctrl+Alt+Win+V
    };

    private void RegisterHotKey(AppSettings settings)
    {
        UnregisterHotKey();

        if (TryRegister(settings.HotkeyModifiers, settings.HotkeyVirtualKey))
        {
            return;
        }

        foreach (var (modifiers, key) in FallbackHotKeys)
        {
            if (TryRegister(modifiers, key))
            {
                settings.HotkeyModifiers = modifiers;
                settings.HotkeyVirtualKey = key;
                _settings.Save();

                _tray.ShowBalloonTip(
                    5000,
                    "ClipBoardX",
                    $"Сочетание занято другим приложением. Вместо него работает {settings.HotkeyLabel}.",
                    ToolTipIcon.Info);

                return;
            }
        }

        _tray.ShowBalloonTip(
            5000,
            "ClipBoardX",
            "Не удалось занять ни одно сочетание клавиш. Открывайте историю через значок в трее.",
            ToolTipIcon.Warning);
    }

    private bool TryRegister(int modifierMask, int virtualKey)
    {
        var modifiers = 0u;
        if ((modifierMask & 1) != 0) modifiers |= NativeMethods.MOD_ALT;
        if ((modifierMask & 2) != 0) modifiers |= NativeMethods.MOD_CONTROL;
        if ((modifierMask & 4) != 0) modifiers |= NativeMethods.MOD_SHIFT;
        if ((modifierMask & 8) != 0) modifiers |= NativeMethods.MOD_WIN;
        modifiers |= NativeMethods.MOD_NOREPEAT;

        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            _listener.Handle,
            (int)NativeMethods.HOTKEY_ID,
            modifiers,
            (uint)virtualKey);

        return _hotkeyRegistered;
    }

    private void UnregisterHotKey()
    {
        if (!_hotkeyRegistered) return;

        NativeMethods.UnregisterHotKey(_listener.Handle, (int)NativeMethods.HOTKEY_ID);
        _hotkeyRegistered = false;
    }

    /// <summary>Перерегистрирует горячую клавишу после изменения настроек.</summary>
    public void ReloadHotKey() => RegisterHotKey(_settings.Current);

    // ── Завершение ─────────────────────────────────────────────────────
    private void ExitApp() => ExitThread();

    protected override void ExitThreadCore()
    {
        _saveTimer.Stop();

        if (_dirty)
        {
            _dirty = false;
            _store.Save();
        }

        UnregisterHotKey();

        _tray.Visible = false;
        _tray.Dispose();

        _listener.ClipboardUpdated -= OnClipboardUpdated;
        _listener.DestroyHandle();

        _form.Dispose();

        base.ExitThreadCore();
    }
}