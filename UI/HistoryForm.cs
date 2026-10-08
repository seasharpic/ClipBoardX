using System.Drawing.Drawing2D;
using ClipBoardX.Models;
using ClipBoardX.Services;

namespace ClipBoardX.UI;

/// <summary>Окно истории буфера обмена.</summary>
internal sealed class HistoryForm : Form
{
    private readonly HistoryStore _store;
    private readonly SettingsService _settings;
    private readonly AppContextHost _host;

    private readonly ListView _list = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly Panel _preview = new();
    private readonly TextBox _previewText = new();
    private readonly PictureBox _previewImage = new();
    private readonly Label _status = new();
    private readonly Button _pasteButton = new();
    private readonly Button _pinButton = new();

    private List<ClipItem> _view = new();
    private ClipItem? _selected;
    private bool _suppressEvents;

    private const string NoSelectionText = "Нет выбранных записей";

    public HistoryForm(HistoryStore store, SettingsService settings, AppContextHost host)
    {
        _store = store;
        _settings = settings;
        _host = host;

        BuildLayout();
        WireEvents();
        RefreshList();
    }

    private void BuildLayout()
    {
        Text = "ClipBoardX — история буфера";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(760, 520);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(32, 33, 36);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;

        // ── Панель поиска ──────────────────────────────────────────────
        var top = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8) };
        top.BackColor = Color.FromArgb(38, 40, 44);

        var searchIcon = new Label
        {
            Text = "🔍",
            AutoSize = true,
            Location = new Point(14, 20),
            Font = new Font("Segoe UI Emoji", 11F)
        };

        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = Color.FromArgb(52, 55, 61);
        _search.ForeColor = Color.WhiteSmoke;
        _search.Font = new Font("Segoe UI", 10F);
        _search.SetBounds(42, 16, 424, 24);
        _search.PlaceholderText = "Поиск по истории…  (Ctrl+F)";

        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.FlatStyle = FlatStyle.Flat;
        _filter.BackColor = Color.FromArgb(52, 55, 61);
        _filter.ForeColor = Color.WhiteSmoke;
        _filter.SetBounds(478, 14, 130, 28);
        _filter.Items.AddRange(new object[]
        {
            "Всё", "Текст", "HTML", "Изображения", "Файлы", "★ Закреплённые"
        });
        _filter.SelectedIndex = 0;

        var clearButton = MakeButton("Очистить", 616, 14, 128);
        clearButton.Click += (_, _) => ClearHistory();

        top.Controls.AddRange(new Control[] { searchIcon, _search, _filter, clearButton });

        // ── Список записей ──────────────────────────────────────────────
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Color.FromArgb(28, 29, 32);
        _list.ForeColor = Color.WhiteSmoke;
        _list.GridLines = false;

        _list.Columns.Add("Пин", 44, HorizontalAlignment.Center);
        _list.Columns.Add("Содержимое", 380, HorizontalAlignment.Left);
        _list.Columns.Add("Тип", 78, HorizontalAlignment.Center);
        _list.Columns.Add("Дата", 128, HorizontalAlignment.Center);

        // Заголовки скрываем: всё рисуем сами в DrawItem.
        _list.HeaderStyle = ColumnHeaderStyle.None;
        _list.OwnerDraw = true;
        _list.DrawItem += OnListDrawItem;

        // ── Панель предпросмотра ────────────────────────────────────────
        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.FromArgb(38, 40, 44);
        _preview.Padding = new Padding(10);

        _previewText.Dock = DockStyle.Fill;
        _previewText.Multiline = true;
        // TextBox не поддерживает прозрачный фон — повторяем цвет панели.
        _previewText.BorderStyle = BorderStyle.None;
        _previewText.BackColor = Color.FromArgb(38, 40, 44);
        _previewText.ForeColor = Color.FromArgb(200, 204, 210);
        _previewText.Font = new Font("Consolas", 9.5F);
        _previewText.ReadOnly = true;
        _previewText.TabStop = false;
        // Полосы прокрутки портили бы тёмную тему; текст прокручивается колесом мыши.
        _previewText.ScrollBars = ScrollBars.None;
        _previewText.Text = NoSelectionText;
        _previewText.WordWrap = true;

        _previewImage.Dock = DockStyle.Fill;
        _previewImage.SizeMode = PictureBoxSizeMode.Zoom;
        _previewImage.BackColor = Color.FromArgb(38, 40, 44);
        _previewImage.Visible = false;

        // Индекс 0 в коллекции — самый верхний по z-order,
        // поэтому PictureBox добавляем последним, чтобы TextBox его перекрывал.
        _preview.Controls.Add(_previewText);
        _preview.Controls.Add(_previewImage);

        // ── Нижняя панель кнопок ────────────────────────────────────────
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        bottom.BackColor = Color.FromArgb(38, 40, 44);

        _pasteButton.Text = "Вставить  (Ctrl+Enter)";
        _pasteButton.SetBounds(8, 10, 180, 32);
        StyleButton(_pasteButton, accent: true);
        _pasteButton.Click += (_, _) => PasteToForeground();

        var copyButton = MakeButton("В буфер  (Enter)", 196, 10, 128);
        copyButton.Click += (_, _) => CopySelected();

        _pinButton.Text = "★ Закрепить";
        _pinButton.SetBounds(332, 10, 116, 32);
        StyleButton(_pinButton, accent: false);
        _pinButton.Click += (_, _) => TogglePin();

        var deleteButton = MakeButton("Удалить  (Del)", 456, 10, 112);
        deleteButton.Click += (_, _) => DeleteSelected();

        var settingsButton = MakeButton("⚙", 576, 10, 40);
        settingsButton.Font = new Font("Segoe UI Symbol", 11F);
        settingsButton.Click += (_, _) => _host.ShowSettings();

        bottom.Controls.AddRange(new Control[]
        {
            _pasteButton, copyButton, _pinButton, deleteButton, settingsButton
        });

        _status.Dock = DockStyle.Bottom;
        _status.Height = 22;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(10, 0, 0, 0);
        _status.ForeColor = Color.FromArgb(150, 155, 165);
        _status.Font = new Font("Segoe UI", 8.5F);

        // Нижний контейнер фиксированной высоты: статус, предпросмотр, кнопки.
        // Так предпросмотр гарантированно не наезжает на список.
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = _status.Height + 132 + bottom.Height,
            BackColor = Color.FromArgb(38, 40, 44)
        };

        footer.Controls.Add(_preview);
        footer.Controls.Add(_status);
        footer.Controls.Add(bottom);

        Controls.Add(_list);
        Controls.Add(footer);
        Controls.Add(top);
    }

    private void WireEvents()
    {
        _search.TextChanged += (_, _) =>
        {
            if (!_suppressEvents) RefreshList();
        };

        _filter.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressEvents) RefreshList();
        };

        _list.SelectedIndexChanged += (_, _) => UpdateSelection();
        _list.DoubleClick += (_, _) => CopySelected();
        _list.KeyDown += OnListKeyDown;

        _list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            var hit = _list.GetItemAt(e.X, e.Y);
            var index = hit is null ? -1 : _list.Items.IndexOf(hit);
            if (index >= 0 && index < _view.Count)
            {
                _suppressEvents = true;
                _list.SelectedItems.Clear();
                _list.Items[index].Selected = true;
                _list.Items[index].EnsureVisible();
                _suppressEvents = false;
                _selected = _view[index];
                UpdatePreview();
            }

            if (_selected is not null) ShowContextMenu(e.Location);
        };

        KeyDown += OnFormKeyDown;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            CopySelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Hide();
                _host.ExitRequested = false;
                e.Handled = true;
                break;

            case Keys.Enter when e.Control:
                PasteToForeground();
                e.Handled = true;
                break;

            case Keys.F when e.Control:
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
                break;

            case Keys.F3:
                _search.Focus();
                e.Handled = true;
                break;
        }
    }

    private Button MakeButton(string text, int x, int y, int width)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 32),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };

        StyleButton(button, accent: false);
        return button;
    }

    private static void StyleButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = accent ? Color.FromArgb(0, 120, 212) : Color.FromArgb(56, 59, 66);
        button.ForeColor = Color.White;
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(0, 99, 172) : Color.FromArgb(74, 78, 86);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 46, 52);
    }

    // ── Отрисовка списка ───────────────────────────────────────────────
    private void OnListDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;

        var index = e.Item != null ? _view.FindIndex(v => v.Id == e.Item!.Text) : -1;
        if (index < 0) return;

        var item = _view[index];
        var bounds = e.Bounds;
        var selected = item == _selected;

        using var backBrush = new SolidBrush(selected ? Color.FromArgb(0, 95, 184) : Color.FromArgb(28, 29, 32));
        e.Graphics.FillRectangle(backBrush, bounds);

        var textColor = selected ? Color.White : Color.FromArgb(225, 228, 233);
        var kindColor = selected ? Color.FromArgb(215, 230, 255) : Color.FromArgb(150, 190, 255);
        var dateColor = selected ? Color.FromArgb(215, 225, 240) : Color.FromArgb(160, 165, 175);
        var pinColor = Color.FromArgb(255, 196, 0);

        var font = Font;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        var pinRect = new Rectangle(bounds.X, bounds.Y, _list.Columns[0].Width, bounds.Height);
        if (item.Pinned)
        {
            TextRenderer.DrawText(e.Graphics, "★", font, pinRect, pinColor, flags);
        }

        var contentRect = new Rectangle(pinRect.Right + 4, bounds.Y, _list.Columns[1].Width, bounds.Height);
        TextRenderer.DrawText(e.Graphics, item.Preview, font, contentRect, textColor, flags);

        var kindRect = new Rectangle(contentRect.Right, bounds.Y, _list.Columns[2].Width, bounds.Height);
        TextRenderer.DrawText(e.Graphics, KindLabel(item.Kind), font, kindRect, kindColor, flags);

        var dateRect = new Rectangle(kindRect.Right, bounds.Y, _list.Columns[3].Width, bounds.Height);
        TextRenderer.DrawText(e.Graphics, $"{item.DayLabel}  {item.TimeLabel}", font, dateRect, dateColor, flags);

        using var linePen = new Pen(Color.FromArgb(48, 50, 56));
        e.Graphics.DrawLine(linePen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    private static string KindLabel(ClipKind kind) => kind switch
    {
        ClipKind.Text => "Текст",
        ClipKind.Html => "HTML",
        ClipKind.Image => "Картинка",
        ClipKind.Files => "Файлы",
        _ => "—"
    };

    // ── Обновление данных ──────────────────────────────────────────────
    private void RefreshList()
    {
        var previousId = _selected?.Id;
        var query = _search.Text.Trim();
        var filterIndex = _filter.SelectedIndex;

        IEnumerable<ClipItem> items = _store.Items;

        switch (filterIndex)
        {
            case 1: items = items.Where(i => i.Kind == ClipKind.Text); break;
            case 2: items = items.Where(i => i.Kind == ClipKind.Html); break;
            case 3: items = items.Where(i => i.Kind == ClipKind.Image); break;
            case 4: items = items.Where(i => i.Kind == ClipKind.Files); break;
            case 5: items = items.Where(i => i.Pinned); break;
        }

        if (query.Length > 0)
        {
            items = items.Where(i => Matches(i, query));
        }

        _view = items
            .OrderByDescending(i => i.Pinned)
            .ThenByDescending(i => i.CreatedUtc)
            .ToList();

        _suppressEvents = true;
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var item in _view)
        {
            _list.Items.Add(new ListViewItem(item.Id));
        }

        _list.EndUpdate();

        var targetIndex = previousId is null ? -1 : _view.FindIndex(i => i.Id == previousId);
        if (targetIndex < 0 && _view.Count > 0) targetIndex = 0;

        if (targetIndex >= 0)
        {
            _list.Items[targetIndex].Selected = true;
            _list.Items[targetIndex].EnsureVisible();
        }

        _suppressEvents = false;

        _selected = targetIndex >= 0 ? _view[targetIndex] : null;

        _status.Text = $"Записей: {_view.Count} из {_store.Items.Count}   •   Горячая клавиша: {_settings.Current.HotkeyLabel}";
        UpdatePreview();
    }

    private static bool Matches(ClipItem item, string query)
    {
        if (item.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var file in item.Files)
        {
            if (file.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void UpdateSelection()
    {
        if (_suppressEvents) return;

        _selected = _list.SelectedItems.Count > 0
            ? _view[_list.SelectedItems[0].Index]
            : null;

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _previewImage.Image?.Dispose();
        _previewImage.Image = null;
        _previewImage.Visible = false;

        if (_selected is null)
        {
            _previewText.Text = NoSelectionText;
            _pasteButton.Enabled = false;
            _pinButton.Enabled = false;
            return;
        }

        _pasteButton.Enabled = true;
        _pinButton.Enabled = true;
        _pinButton.Text = _selected.Pinned ? "★ Закреплено" : "★ Закрепить";

        if (_selected.Kind == ClipKind.Image)
        {
            if (!string.IsNullOrEmpty(_selected.ImagePath) && File.Exists(_selected.ImagePath))
            {
                try
                {
                    using var source = Image.FromFile(_selected.ImagePath);
                    _previewImage.Image = new Bitmap(source);
                    _previewImage.Visible = true;
                    _previewText.Text = _selected.Preview;
                    return;
                }
                catch (Exception)
                {
                    // Файл недоступен — покажем текстовую заглушку.
                }
            }

            _previewText.Text = "Изображение недоступно на диске";
            return;
        }

        _previewText.Text = _selected.Kind == ClipKind.Files && _selected.Files.Count > 0
            ? string.Join(Environment.NewLine, _selected.Files.Take(20))
              + (_selected.Files.Count > 20
                  ? $"{Environment.NewLine}… ещё {_selected.Files.Count - 20}"
                  : string.Empty)
            : _selected.Text;
    }

    // ── Действия ───────────────────────────────────────────────────────
    private void CopySelected()
    {
        if (_selected is null) return;

        ClipboardWriter.Write(_selected);
        _selected.UseCount++;
        _selected.CreatedUtc = DateTime.UtcNow;

        _host.MarkDirty();
        Hide();
        _host.ExitRequested = false;
    }

    private void PasteToForeground()
    {
        if (_selected is null) return;

        var target = _host.LastForegroundWindow;

        ClipboardWriter.Write(_selected);
        _selected.UseCount++;
        _selected.CreatedUtc = DateTime.UtcNow;
        _host.MarkDirty();

        Hide();
        _host.ExitRequested = false;

        if (target != IntPtr.Zero)
        {
            ClipboardWriter.SendPaste(target);
        }
    }

    private void TogglePin()
    {
        if (_selected is null) return;

        _selected.Pinned = !_selected.Pinned;
        _host.MarkDirty();
        RefreshList();
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;

        var item = _selected;
        _selected = null;
        _store.RemoveItem(item, deleteImageFile: true);
        _host.MarkDirty();
        RefreshList();
    }

    private void ClearHistory()
    {
        if (_store.Items.Count == 0) return;

        var result = MessageBox.Show(
            this,
            "Удалить все незакреплённые записи из истории?",
            "ClipBoardX",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (result != DialogResult.OK) return;

        _store.Clear(includePinned: false);
        _host.MarkDirty();
        RefreshList();
    }

    private void ShowContextMenu(Point location)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(45, 47, 53),
            ForeColor = Color.White,
            Font = Font,
            ShowImageMargin = false
        };

        var copyItem = new ToolStripMenuItem("Скопировать в буфер");
        copyItem.Click += (_, _) => CopySelected();

        var pasteItem = new ToolStripMenuItem("Вставить в активное приложение");
        pasteItem.Click += (_, _) => PasteToForeground();

        var pinItem = new ToolStripMenuItem(_selected!.Pinned ? "Снять закрепление" : "Закрепить");
        pinItem.Click += (_, _) => TogglePin();

        var deleteItem = new ToolStripMenuItem("Удалить");
        deleteItem.Click += (_, _) => DeleteSelected();

        menu.Items.AddRange(new ToolStripItem[] { copyItem, pasteItem, new ToolStripSeparator(), pinItem, deleteItem });

        menu.Show(_list, location);
    }

    /// <summary>Обновляет список извне (после изменения истории).</summary>
    public void RefreshListPublic() => RefreshList();

    /// <summary>Переключает фильтр по индексу и сбрасывает поиск.</summary>
    public void OpenFilter(int filterIndex)
    {
        _suppressEvents = true;
        _search.Clear();
        _filter.SelectedIndex = filterIndex;
        _suppressEvents = false;
        RefreshList();
    }

    // ── Показ окна ─────────────────────────────────────────────────────
    public void ShowNearCursor()
    {
        var cursor = Cursor.Position;
        var work = Screen.FromPoint(cursor).WorkingArea;

        var x = Math.Clamp(cursor.X - Width / 2, work.Left, Math.Max(work.Left, work.Right - Width));
        var y = cursor.Y - Height - 12;

        // Если снизу не помещается — показываем под курсором.
        if (y < work.Top) y = Math.Min(cursor.Y + 16, Math.Max(work.Top, work.Bottom - Height));

        Location = new Point(x, y);

        if (!Visible) Show();
        else if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

        // SetWindowPos надёжнее SetForegroundWindow: другие приложения
        // могут отказывать в передаче фокуса.
        NativeMethods.SetWindowPos(
            Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);

        Activate();
        BringToFront();
        NativeMethods.SetForegroundWindow(Handle);

        // Фокус на списке, чтобы был виден текст-подсказка в строке поиска.
        _list.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Enter вне строки поиска — вернуть запись в буфер.
        if (keyData == Keys.Enter && !ModifierKeys.HasFlag(Keys.Control))
        {
            CopySelected();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(34, 36, 40),
            Color.FromArgb(26, 27, 30),
            LinearGradientMode.Vertical);

        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}