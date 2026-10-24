using System.Diagnostics;
using Microsoft.Win32;

namespace LazyASF;

/// <summary>Маленькое окно на ноуте: код сопряжения, адрес, статус и лог. Закрытие = свернуть в трей.</summary>
public sealed class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(18, 18, 24), Card = Color.FromArgb(30, 30, 40),
        Text1 = Color.FromArgb(240, 240, 245), Text2 = Color.FromArgb(150, 150, 165), Accent = Color.FromArgb(120, 140, 255);

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Server _server;
    readonly Orchestrator _orch;
    readonly AppConfig _cfg;
    readonly NotifyIcon _tray;
    readonly Label _phones = new(), _session = new();
    readonly TextBox _log = new();
    bool _exiting;

    public MainForm(Server server, Orchestrator orch, AppConfig cfg, bool startHidden)
    {
        _server = server; _orch = orch; _cfg = cfg;
        Text = "LazyASF";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Bg; ForeColor = Text1;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(520, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24), AutoScroll = true };
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "LazyASF", Font = new Font("Segoe UI Semibold", 22f), AutoSize = true, ForeColor = Text1 });
        layout.Controls.Add(new Label { Text = "Пульт для телевизора с дивана", AutoSize = true, ForeColor = Text2, Margin = new Padding(3, 0, 3, 16) });

        var pinCard = new FlowLayoutPanel
        {
            BackColor = Card, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            MinimumSize = new Size(470, 0), Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12)
        };
        pinCard.Controls.Add(new Label { Text = "Код для телефона", ForeColor = Text2, AutoSize = true });
        pinCard.Controls.Add(new Label
        {
            Text = string.Join(" ", server.Pin.ToCharArray()), Font = new Font("Segoe UI Semibold", 34f), ForeColor = Accent, AutoSize = true
        });
        pinCard.Controls.Add(new Label
        {
            Text = $"{string.Join(", ", Server.LocalIPs())} · порт {cfg.Port}", ForeColor = Text2, AutoSize = true
        });
        layout.Controls.Add(pinCard);

        foreach (var l in new[] { _phones, _session }) { l.AutoSize = true; l.ForeColor = Text1; l.Margin = new Padding(3, 2, 3, 2); layout.Controls.Add(l); }

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        buttons.Controls.Add(MakeButton("Вернуть всё как было", (_, _) => _ = Task.Run(() => _orch.Restore("кнопка на ноуте"))));
        buttons.Controls.Add(MakeButton("config.json", (_, _) => Process.Start(new ProcessStartInfo(AppConfig.FilePath) { UseShellExecute = true })));
        buttons.Controls.Add(MakeButton("В трей", (_, _) => Hide()));
        layout.Controls.Add(buttons);

        var autostart = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true, ForeColor = Text2, Checked = IsAutostart() };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);
        layout.Controls.Add(autostart);

        _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Card; _log.ForeColor = Text2; _log.BorderStyle = BorderStyle.None;
        _log.Font = new Font("Consolas", 9f); _log.Size = new Size(470, 220); _log.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_log);

        _tray = new NotifyIcon { Icon = Icon, Text = "LazyASF", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Items.Add("Показать", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("Вернуть всё как было", null, (_, _) => _ = Task.Run(() => _orch.Restore("меню трея")));
        _tray.ContextMenuStrip.Items.Add("Выход", null, (_, _) => { _exiting = true; Close(); });
        _tray.DoubleClick += (_, _) => ShowFromTray();

        Log.Line += line => UI(() => { _log.AppendText(line + Environment.NewLine); });
        _server.ClientsChanged += () => UI(RefreshStatus);
        _orch.Changed += () => UI(RefreshStatus);
        RefreshStatus();

        if (startHidden) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; Load += (_, _) => Hide(); }
    }

    Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Text1,
            Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
        b.Click += onClick;
        return b;
    }

    void UI(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }

    void RefreshStatus()
    {
        var phones = _server.ConnectedDevices;
        _phones.Text = phones.Count == 0 ? "📱 Телефон не подключён" : "📱 Подключён: " + string.Join(", ", phones);
        var s = _orch.State;
        var sleep = s.SleepEndsAt is long ms ? $" · сон в {DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime:HH:mm}" : "";
        _session.Text = (s.Active ? $"📺 Режим ТВ: {s.Site}" : "📺 Режим ТВ выключен") + (s.Busy ? " (выполняется…)" : "") + sleep;
    }

    void ShowFromTray()
    {
        Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "LazyASF", "Работаю в трее — телефон может подключаться", ToolTipIcon.Info);
            return;
        }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("LazyASF") is not null;
    }

    static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("LazyASF", $"\"{Environment.ProcessPath}\" --tray");
        else k.DeleteValue("LazyASF", false);
    }
}
