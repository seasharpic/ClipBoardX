using ClipBoardX.Models;
using ClipBoardX.Services;

namespace ClipBoardX.UI;

/// <summary>Диалог настроек ClipBoardX.</summary>
internal sealed class SettingsForm : Form
{
    private readonly SettingsService _service;
    private readonly AppContextHost _host;

    private readonly NumericUpDown _maxItems = new();
    private readonly NumericUpDown _expireDays = new();
    private readonly NumericUpDown _minTextLength = new();
    private readonly CheckBox _captureImages = new();
    private readonly CheckBox _captureFiles = new();
    private readonly CheckBox _captureHtml = new();
    private readonly CheckBox _runAtStartup = new();
    private readonly CheckBox _startMinimized = new();
    private readonly HotkeyCaptureBox _hotkey = new();

    public SettingsForm(SettingsService service, AppContextHost host)
    {
        _service = service;
        _host = host;

        Text = "ClipBoardX — настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(440, 396);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(38, 40, 44);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        var settings = service.Current;

        BuildLayout();

        _maxItems.Value = Clamp(settings.MaxItems, 10, 5000);
        _expireDays.Value = Clamp(settings.ExpireDays, 0, 365);
        _minTextLength.Value = Clamp(settings.MinTextLength, 0, 1000);
        _captureImages.Checked = settings.CaptureImages;
        _captureFiles.Checked = settings.CaptureFiles;
        _captureHtml.Checked = settings.CaptureHtml;
        _runAtStartup.Checked = settings.RunAtStartup;
        _startMinimized.Checked = settings.StartMinimized;
        _hotkey.SetHotkey(settings.HotkeyModifiers, settings.HotkeyVirtualKey);

        Load += (_, _) => OkButton.Focus();
    }

    private Button OkButton { get; set; } = new();

    private void BuildLayout()
    {
        var y = 18;

        y = AddGroupLabel("История", y);
        y = AddNumberRow("Записей хранить:", ref y, _maxItems, "шт.");
        y = AddNumberRow("Удалять старше:", ref y, _expireDays, "дней  (0 — не удалять)");
        y = AddNumberRow("Игнорировать текст короче:", ref y, _minTextLength, "символов  (0 — отключить)");

        y += 12;
        y = AddGroupLabel("Что захватывать", y);
        y = AddCheckBox("Изображения", _captureImages, ref y);
        y = AddCheckBox("Списки файлов", _captureFiles, ref y);
        y = AddCheckBox("HTML-формат (очищенный текст)", _captureHtml, ref y);

        y += 12;
        y = AddGroupLabel("Запуск и буфер", y);
        y = AddCheckBox("Запускать вместе с Windows", _runAtStartup, ref y);
        y = AddCheckBox("При автозапуске не показывать окно", _startMinimized, ref y);

        _hotkey.BackColor = Color.FromArgb(52, 55, 61);
        _hotkey.ForeColor = Color.FromArgb(150, 190, 255);
        _hotkey.SetBounds(190, y, 150, 24);
        _hotkey.Cursor = Cursors.Hand;
        new ToolTip().SetToolTip(_hotkey, "Нажмите и отпустите клавиши — например Ctrl+Shift+V");
        Controls.Add(_hotkey);

        var hotkeyCaption = new Label
        {
            Text = "Горячая клавиша:",
            ForeColor = Color.FromArgb(200, 204, 210),
            Location = new Point(18, y + 2),
            AutoSize = true
        };
        Controls.Add(hotkeyCaption);

        var hotkeyHint = new Label
        {
            Text = "кликните и нажмите клавиши",
            ForeColor = Color.FromArgb(140, 145, 155),
            Font = new Font("Segoe UI", 8F),
            Location = new Point(348, y + 5),
            AutoSize = true
        };
        Controls.Add(hotkeyHint);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(10)
        };

        var openDataButton = new Button
        {
            Text = "Папка данных",
            Size = new Size(120, 30),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        StyleButton(openDataButton);
        openDataButton.Click += (_, _) =>
        {
            AppPaths.EnsureCreated();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.DataDir)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                MessageBox.Show(this, "Не удалось открыть папку:\n" + AppPaths.DataDir, "ClipBoardX");
            }
        };

        OkButton = new Button
        {
            Text = "Сохранить",
            Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.None
        };
        StyleButton(OkButton, accent: true);
        OkButton.Click += (_, _) => Save();

        var cancelButton = new Button
        {
            Text = "Отмена",
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        StyleButton(cancelButton);

        buttons.Controls.Add(OkButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(openDataButton);

        Controls.Add(buttons);
    }

    private int AddGroupLabel(string text, int y)
    {
        var label = new Label
        {
            Text = text.ToUpperInvariant(),
            ForeColor = Color.FromArgb(130, 170, 255),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Location = new Point(18, y),
            AutoSize = true
        };

        Controls.Add(label);
        return y + 24;
    }

    private int AddNumberRow(string caption, ref int y, NumericUpDown control, string suffix)
    {
        var label = new Label
        {
            Text = caption,
            ForeColor = Color.FromArgb(200, 204, 210),
            Location = new Point(18, y + 3),
            AutoSize = true
        };
        Controls.Add(label);

        control.Minimum = 0;
        control.Maximum = 5000;
        control.BorderStyle = BorderStyle.FixedSingle;
        control.BackColor = Color.FromArgb(52, 55, 61);
        control.ForeColor = Color.WhiteSmoke;
        control.SetBounds(190, y, 120, 24);
        Controls.Add(control);

        var hint = new Label
        {
            Text = suffix,
            ForeColor = Color.FromArgb(140, 145, 155),
            Font = new Font("Segoe UI", 8F),
            Location = new Point(320, y + 5),
            AutoSize = true
        };
        Controls.Add(hint);

        y += 30;
        return y;
    }

    private int AddCheckBox(string text, CheckBox box, ref int y)
    {
        box.Text = text;
        box.ForeColor = Color.FromArgb(210, 214, 220);
        box.AutoSize = true;
        box.Location = new Point(18, y + 2);
        box.Cursor = Cursors.Hand;
        box.FlatStyle = FlatStyle.Flat;
        box.FlatAppearance.BorderSize = 0;
        box.BackColor = Color.FromArgb(38, 40, 44);
        box.ForeColor = Color.FromArgb(210, 214, 220);
        box.UseVisualStyleBackColor = false;

        Controls.Add(box);
        y += 26;
        return y;
    }

    private static void StyleButton(Button button, bool accent = false)
    {
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = accent ? Color.FromArgb(0, 120, 212) : Color.FromArgb(56, 59, 66);
        button.ForeColor = Color.White;
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(0, 99, 172) : Color.FromArgb(74, 78, 86);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 46, 52);
    }

    private static decimal Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private void Save()
    {
        var settings = _service.Current;

        settings.MaxItems = (int)_maxItems.Value;
        settings.ExpireDays = (int)_expireDays.Value;
        settings.MinTextLength = (int)_minTextLength.Value;
        settings.CaptureImages = _captureImages.Checked;
        settings.CaptureFiles = _captureFiles.Checked;
        settings.CaptureHtml = _captureHtml.Checked;
        settings.StartMinimized = _startMinimized.Checked;
        settings.HotkeyModifiers = _hotkey.Modifiers;
        settings.HotkeyVirtualKey = _hotkey.VirtualKey;

        if (settings.RunAtStartup != _runAtStartup.Checked)
        {
            settings.RunAtStartup = _runAtStartup.Checked;
            _service.ApplyRunAtStartup(settings.RunAtStartup);
        }

        _service.Save();
        _host.ReloadHotKey();
        _host.MarkDirty();

        DialogResult = DialogResult.OK;
        Close();
    }
}