using LazyASF.Native;

namespace LazyASF;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Диагностика без окна: LazyASF.exe --diag  → пишет в log.txt устройства звука/Bluetooth/экраны
        if (args.Contains("--diag")) { Diag(); return 0; }
        // Проверка запуска Chrome без режима ТВ: LazyASF.exe --open https://...
        if (args.Length == 2 && args[0] == "--open")
        {
            var w = Chrome.Open(AppConfig.Load(), args[1]).GetAwaiter().GetResult();
            return w is null ? 1 : 0;
        }

        using var mutex = new Mutex(true, "LazyASF.SingleInstance", out bool first);
        if (!first) { MessageBox.Show("LazyASF уже запущен — ищи значок в трее.", "LazyASF"); return 1; }

        ApplicationConfiguration.Initialize();
        var cfg = AppConfig.Load();
        var orch = new Orchestrator(cfg);
        var server = new Server(cfg, orch);
        try { server.Start().GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            MessageBox.Show($"Не удалось запустить сервер на порту {cfg.Port}:\n{e.Message}", "LazyASF", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
        Application.Run(new MainForm(server, orch, cfg, args.Contains("--tray")));
        server.Stop().GetAwaiter().GetResult();
        return 0;
    }

    static void Diag()
    {
        var cfg = AppConfig.Load();
        Log.Write("=== DIAG ===");
        Log.Write($"Экран: {Display.Current()}, мониторов: {Display.ScreenCount}");
        Log.Write($"Bluetooth включён: {Bluetooth.IsOn().GetAwaiter().GetResult()}");
        foreach (var d in Bluetooth.Paired()) Log.Write($"BT: {d.Name} audio={d.IsAudio} connected={d.Connected} cod=0x{d.ClassOfDevice:X}");
        foreach (var e in Audio.List()) Log.Write($"Audio: [{e.State}] {e.Name} | {e.DeviceName}");
        Log.Write($"Звук по умолчанию: {Audio.DefaultRenderId()}");
        Log.Write($"Chrome: {Chrome.FindExe(cfg)} профиль={Chrome.ProfileDir(cfg)}");
        Log.Write($"IP: {string.Join(", ", Server.LocalIPs())}");
    }
}
