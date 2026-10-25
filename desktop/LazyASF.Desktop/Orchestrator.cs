using LazyASF.Native;

namespace LazyASF;

public sealed class StepInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>pending | running | ok | fail | skip</summary>
    public string Status { get; set; } = "pending";
    public string? Detail { get; set; }
}

public sealed class SessionState
{
    public bool Active { get; set; }
    public bool Busy { get; set; }
    public string? Site { get; set; }
    public string Headphones { get; set; } = "";
    public bool HeadphonesConnected { get; set; }
    /// <summary>Куда звук: headphones | tv</summary>
    public string Output { get; set; } = "headphones";
    /// <summary>Unix ms, когда сработает таймер сна; null — таймера нет.</summary>
    public long? SleepEndsAt { get; set; }
    public List<StepInfo> Steps { get; set; } = new();
}

/// <summary>Сценарии «включить режим ТВ», «вернуть всё как было» и таймер сна.</summary>
public sealed class Orchestrator
{
    readonly AppConfig _cfg;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly object _lock = new();
    System.Threading.Timer? _sleepTimer;

    // то, что было до включения режима ТВ
    Display.Topology _prevTopology = Display.Topology.Unknown;
    bool? _prevBtOn;
    string? _prevAudioId;

    public SessionState State { get; } = new();
    public event Action? Changed;

    public Orchestrator(AppConfig cfg)
    {
        _cfg = cfg;
        State.Headphones = cfg.DefaultHeadphones;
    }

    void Notify() => Changed?.Invoke();

    /// <summary>Копия состояния для отправки на телефон (шаги меняются из другого потока).</summary>
    public SessionState Snapshot()
    {
        lock (_lock)
        {
            return new SessionState
            {
                Active = State.Active, Busy = State.Busy, Site = State.Site, Headphones = State.Headphones,
                HeadphonesConnected = State.HeadphonesConnected, SleepEndsAt = State.SleepEndsAt, Output = State.Output,
                Steps = State.Steps.Select(s => new StepInfo { Id = s.Id, Title = s.Title, Status = s.Status, Detail = s.Detail }).ToList(),
            };
        }
    }

    StepInfo Step(string id) => State.Steps.First(s => s.Id == id);

    void Mark(string id, string status, string? detail = null)
    {
        string title;
        lock (_lock) { var s = Step(id); s.Status = status; s.Detail = detail; title = s.Title; }
        if (status is "ok" or "fail") Log.Write($"   {(status == "ok" ? "✓" : "✗")} {title}{(detail is null ? "" : $" — {detail}")}");
        Notify();
    }

    // ---------------------------------------------------------------- launch
    public async Task Launch(string siteKey, string? headphones, string? output)
    {
        if (!_cfg.Sites.TryGetValue(siteKey, out var site)) { Log.Write($"Неизвестный сайт: {siteKey}"); return; }
        if (!await _gate.WaitAsync(0)) { Log.Write("Уже выполняется другая команда"); return; }
        try
        {
            if (!string.IsNullOrWhiteSpace(headphones)) State.Headphones = headphones;
            State.Output = output == "tv" ? "tv" : "headphones";
            bool toTv = State.Output == "tv";
            bool withExtension = siteKey == "youtube" && _cfg.YoutubeExtension.Mode != "none";
            lock (_lock)
            {
                State.Busy = true;
                State.Site = siteKey;
                State.Steps = new()
                {
                    new() { Id = "display", Title = "Только экран телевизора" },
                    new() { Id = "browser", Title = $"Открыть {site.Title}" },
                };
                if (withExtension) State.Steps.Add(new() { Id = "extension", Title = "VPN для YouTube (ЮБуст)" });
                ResetOutputSteps(toTv);
            }
            Notify();
            Log.Write($"▶ Режим ТВ: {site.Title}, звук → {(toTv ? "телевизор" : $"наушники «{State.Headphones}»")}");

            if (!State.Active)
            {
                _prevTopology = Display.Current();
                _prevBtOn = await Bluetooth.IsOn();
                _prevAudioId = Audio.DefaultRenderId();
                Log.Write($"Запомнил: экран={_prevTopology}, bluetooth={_prevBtOn}, звук={_prevAudioId}");
            }
            State.Active = true;

            // 1. экран
            Mark("display", "running");
            if (Display.Current() == Display.Topology.External) Mark("display", "ok", "уже включено");
            else if (Display.Set(Display.Topology.External)) { await Task.Delay(2500); Mark("display", "ok"); }
            else Mark("display", "fail", "Не переключилось — ТВ подключён по HDMI?");

            // 2. браузер
            Mark("browser", "running");
            var window = await Chrome.Open(_cfg, site.Url);
            Mark("browser", window is null ? "fail" : "ok");

            // 3. VPN-расширение для YouTube: подключаем, потом обновляем страницу
            if (withExtension && window is { } win)
            {
                Mark("extension", "running");
                await Task.Delay(_cfg.YoutubeExtension.DelayMs);
                var (ok, detail) = await RunExtension(win);
                Mark("extension", ok ? "ok" : "fail", detail);
                if (ok && win != IntPtr.Zero && Win.Activate(win)) { Input.Combo("f5"); await Task.Delay(1500); }
            }
            else if (withExtension) Mark("extension", "skip");

            if (_cfg.BrowserFullscreen && window is { } w && w != IntPtr.Zero) Chrome.Fullscreen(w);

            // 4. звук
            if (toTv) await ConnectTvAudio();
            else await ConnectHeadphones();
        }
        catch (Exception e)
        {
            Log.Write($"Ошибка запуска: {e}");
        }
        finally
        {
            State.Busy = false;
            Notify();
            _gate.Release();
        }
    }

    void ResetOutputSteps(bool toTv)
    {
        lock (_lock)
        {
            State.Steps.RemoveAll(s => s.Id is "bluetooth" or "headphones" or "audio");
            if (toTv)
            {
                State.Steps.Add(new() { Id = "audio", Title = "Звук на телевизор" });
                return;
            }
            State.Steps.Add(new() { Id = "bluetooth", Title = "Bluetooth на ноуте" });
            State.Steps.Add(new() { Id = "headphones", Title = $"Подключить {State.Headphones}" });
            State.Steps.Add(new() { Id = "audio", Title = "Звук в наушники" });
        }
    }

    /// <summary>Переключить звук на лету: tv | headphones. Также «Повторить наушники» на телефоне.</summary>
    public async Task SwitchOutput(string target, string? headphones)
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            bool toTv = target == "tv";
            State.Output = toTv ? "tv" : "headphones";
            if (!string.IsNullOrWhiteSpace(headphones)) State.Headphones = headphones;
            lock (_lock) State.Busy = true;
            ResetOutputSteps(toTv);
            Notify();
            Log.Write($"🔊 Звук → {(toTv ? "телевизор" : State.Headphones)}");
            if (toTv)
            {
                await ConnectTvAudio();
                // отпускаем наушники, чтобы телефон мог их забрать
                foreach (var ep in Audio.FindHeadphones(State.Headphones).Where(e => e.Active))
                    Audio.BtOneShot(ep, connect: false);
                State.HeadphonesConnected = false;
            }
            else await ConnectHeadphones();
        }
        catch (Exception e) { Log.Write($"Ошибка переключения звука: {e}"); }
        finally
        {
            State.Busy = false;
            Notify();
            _gate.Release();
        }
    }

    async Task<(bool ok, string? detail)> RunExtension(IntPtr window)
    {
        var ext = _cfg.YoutubeExtension;
        switch (ext.Mode)
        {
            case "uia":
                return window == IntPtr.Zero ? (false, "окно Chrome не найдено") : await ExtensionAutomation.Connect(window, ext);
            case "hotkey" when !string.IsNullOrWhiteSpace(ext.Hotkey):
                return (Input.Combo(ext.Hotkey), null);
            case "click":
                Input.MoveTo(ext.ClickX, ext.ClickY);
                await Task.Delay(150);
                Input.Click();
                return (true, null);
            default:
                return (false, "не настроено");
        }
    }

    AudioEndpoint? FindTvAudio()
    {
        var active = Audio.List().Where(e => e.Active).ToList();
        return active.FirstOrDefault(e => !string.IsNullOrWhiteSpace(_cfg.TvAudioDevice)
                                          && e.Name.Contains(_cfg.TvAudioDevice, StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(e => e.DeviceName.Contains("Display Audio", StringComparison.OrdinalIgnoreCase)
                                          || e.Name.Contains("HDMI", StringComparison.OrdinalIgnoreCase)
                                          || e.DeviceName.Contains("HDMI", StringComparison.OrdinalIgnoreCase));
    }

    async Task ConnectTvAudio()
    {
        Mark("audio", "running", "Жду звук по HDMI…");
        AudioEndpoint? tv = null;
        // после переключения экрана HDMI-звук появляется через пару секунд
        for (int i = 0; i < 15 && (tv = FindTvAudio()) is null; i++) await Task.Delay(1000);
        if (tv is null) { Mark("audio", "fail", "Не нашёл звук телевизора — проверь tvAudioDevice в config.json"); return; }
        Mark("audio", Audio.SetDefault(tv.Id) ? "ok" : "fail", tv.Name);
    }

    async Task ConnectHeadphones()
    {
        Mark("bluetooth", "running");
        if (!await Bluetooth.SetOn(true)) { Mark("bluetooth", "fail", "Не удалось включить"); Mark("headphones", "skip"); Mark("audio", "skip"); return; }
        Mark("bluetooth", "ok");
        await Task.Delay(1500);

        Mark("headphones", "running", "Жду наушники…");
        var name = State.Headphones;
        var deadline = DateTime.UtcNow.AddSeconds(_cfg.HeadphonesConnectTimeoutSec);
        bool serviceToggled = false;
        int attempt = 0;
        AudioEndpoint? active = null;
        while (DateTime.UtcNow < deadline)
        {
            var eps = Audio.FindHeadphones(name);
            active = eps.FirstOrDefault(e => e.Active && !e.IsHandsFree) ?? eps.FirstOrDefault(e => e.Active);
            if (active is not null) break;

            attempt++;
            var target = eps.FirstOrDefault(e => !e.IsHandsFree) ?? eps.FirstOrDefault();
            bool kicked = target is not null && Audio.BtOneShot(target, connect: true);
            if ((!kicked || attempt >= 3) && !serviceToggled)
            {
                serviceToggled = true;
                Mark("headphones", "running", "Пробую через службу Bluetooth…");
                await Task.Run(() => Bluetooth.ToggleAudioService(name));
            }
            else Mark("headphones", "running", $"Попытка {attempt}…");
            await Task.Delay(4000);
        }

        if (active is null)
        {
            State.HeadphonesConnected = false;
            Mark("headphones", "fail", "Не подключились. Наушники в зоне и отвязаны от телефона?");
            Mark("audio", "skip");
            return;
        }
        State.HeadphonesConnected = true;
        Mark("headphones", "ok", active.Name);

        Mark("audio", "running");
        await Task.Delay(800);
        Mark("audio", Audio.SetDefault(active.Id) ? "ok" : "fail");
    }

    // ---------------------------------------------------------------- restore
    public async Task Restore(string reason)
    {
        await _gate.WaitAsync();
        try
        {
            Log.Write($"⟲ Возвращаю как было ({reason})");
            CancelSleep(notify: false);
            State.Busy = true;
            Notify();

            // отпускаем наушники, чтобы телефон мог их забрать
            foreach (var ep in Audio.FindHeadphones(State.Headphones).Where(e => e.Active))
                Audio.BtOneShot(ep, connect: false);
            State.HeadphonesConnected = false;

            if (_prevAudioId is not null && Audio.List().Any(e => e.Id == _prevAudioId && e.Active))
                Audio.SetDefault(_prevAudioId);

            if (_prevBtOn == false) await Bluetooth.SetOn(false);

            var topo = _prevTopology is Display.Topology.Unknown or Display.Topology.External
                ? Display.Topology.Internal : _prevTopology;
            if (State.Active || Display.Current() == Display.Topology.External) Display.Set(topo);

            State.Active = false;
            State.Site = null;
            State.Steps = new();
        }
        catch (Exception e) { Log.Write($"Ошибка возврата: {e}"); }
        finally
        {
            State.Busy = false;
            Notify();
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- sleep timer
    public void SetSleep(int minutes)
    {
        if (minutes <= 0) { CancelSleep(); return; }
        _sleepTimer?.Dispose();
        var due = TimeSpan.FromMinutes(minutes);
        State.SleepEndsAt = DateTimeOffset.UtcNow.Add(due).ToUnixTimeMilliseconds();
        _sleepTimer = new System.Threading.Timer(_ => _ = SleepFired(), null, due, Timeout.InfiniteTimeSpan);
        Log.Write($"⏾ Таймер сна: {minutes} мин (до {DateTime.Now.Add(due):HH:mm})");
        Notify();
    }

    public void CancelSleep(bool notify = true)
    {
        _sleepTimer?.Dispose();
        _sleepTimer = null;
        if (State.SleepEndsAt is not null && notify) Log.Write("Таймер сна отменён");
        State.SleepEndsAt = null;
        if (notify) Notify();
    }

    async Task SleepFired()
    {
        Log.Write("⏾ Таймер сна сработал");
        Input.Combo("playpause");
        await Restore("таймер сна");
        await TvControl.PowerOff(_cfg);
        await Task.Delay(3000);
        Log.Write($"Действие ноута: {_cfg.SleepTimerPowerAction}");
        Power.Do(_cfg.SleepTimerPowerAction);
    }
}
