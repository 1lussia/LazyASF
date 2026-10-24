using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazyASF;

public sealed class SiteConfig
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Что сделать с расширением YouTube после открытия сайта. ЗАГЛУШКА — ждём данные от пользователя.</summary>
public sealed class ExtensionConfig
{
    /// <summary>none | hotkey | click</summary>
    public string Mode { get; set; } = "none";
    /// <summary>Например "Alt+Shift+Y" (назначается в chrome://extensions/shortcuts).</summary>
    public string Hotkey { get; set; } = "";
    /// <summary>Координаты клика по иконке расширения на экране ТВ (для mode=click).</summary>
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public int DelayMs { get; set; } = 4000;
    public string Note { get; set; } = "TODO: название расширения и что именно нажимать";
}

public sealed class TvConfig
{
    /// <summary>IP телевизора Samsung в домашней сети. Пусто = не пытаться выключать по сети.</summary>
    public string Ip { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class AppConfig
{
    public int Port { get; set; } = 47800;
    public int DiscoveryPort { get; set; } = 47801;

    public string ChromePath { get; set; } = "";
    /// <summary>Имя профиля Chrome (как в Chrome) — папка профиля ищется автоматически.</summary>
    public string ChromeProfileName { get; set; } = "Илья Сериков";
    /// <summary>Если задано — используется как есть (например "Default").</summary>
    public string ChromeProfileDir { get; set; } = "";
    public bool BrowserFullscreen { get; set; } = true;

    public Dictionary<string, SiteConfig> Sites { get; set; } = new()
    {
        ["youtube"] = new() { Title = "YouTube", Url = "https://www.youtube.com/" },
        ["boosty"] = new() { Title = "Boosty", Url = "https://boosty.to/" },
        ["kino"] = new() { Title = "kino.watch", Url = "https://kino.watch/" },
    };

    public ExtensionConfig YoutubeExtension { get; set; } = new();

    /// <summary>Наушники по умолчанию (подстрока имени Bluetooth-устройства).</summary>
    public string DefaultHeadphones { get; set; } = "REDMI Buds 8 Pro";
    public int HeadphonesConnectTimeoutSec { get; set; } = 40;

    /// <summary>Что делать ноуту, когда сработал таймер сна: sleep | hibernate | shutdown | none</summary>
    public string SleepTimerPowerAction { get; set; } = "sleep";
    public TvConfig Tv { get; set; } = new();

    public List<string> PairedTokens { get; set; } = new();

    // ---------- load / save ----------
    [JsonIgnore] public static string Dir => AppContext.BaseDirectory;
    [JsonIgnore] public static string FilePath => Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly object SaveLock = new();

    public static AppConfig Load()
    {
        AppConfig cfg;
        try
        {
            cfg = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new()
                : new();
        }
        catch (Exception e)
        {
            Log.Write($"config.json не прочитался ({e.Message}), беру настройки по умолчанию");
            cfg = new();
        }
        cfg.Save();
        return cfg;
    }

    public void Save()
    {
        lock (SaveLock) File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
