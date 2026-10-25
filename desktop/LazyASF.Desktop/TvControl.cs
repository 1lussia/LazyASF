using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace LazyASF;

/// <summary>
/// Выключение Samsung TV по сети (best effort). Поддерживаются:
///  • Tizen (2016+): WebSocket 8001/8002, при первом подключении ТВ спросит «Разрешить?»;
///  • старые Smart TV (2010–2015): протокол пульта на порту 55000.
/// Если IP не задан в config.json (tv.ip) — ничего не делаем.
/// </summary>
public static class TvControl
{
    const string AppName = "LazyASF";

    public static async Task<bool> PowerOff(AppConfig cfg)
    {
        var tv = cfg.Tv;
        if (!string.IsNullOrWhiteSpace(tv.SmartThingsToken))
        {
            try { if (await SmartThingsOff(cfg)) return true; }
            catch (Exception e) { Log.Write($"SmartThings: {e.Message}"); }
        }
        if (string.IsNullOrWhiteSpace(tv.Ip))
        {
            if (string.IsNullOrWhiteSpace(tv.SmartThingsToken)) Log.Write("ТВ: нет ни ключа SmartThings, ни IP (tv в config.json) — пропускаю");
            return false;
        }
        foreach (var attempt in new Func<string, Task<bool>>[] { ip => Tizen(ip, false), ip => Tizen(ip, true), Legacy })
        {
            try { if (await attempt(tv.Ip)) { Log.Write("ТВ: команда выключения отправлена"); return true; } }
            catch (Exception e) { Log.Write($"ТВ: {e.GetType().Name}: {e.Message}"); }
        }
        return false;
    }

    /// <summary>Выключение через облако SmartThings — работает, даже если ТВ в другой сети.</summary>
    static async Task<bool> SmartThingsOff(AppConfig cfg)
    {
        var tv = cfg.Tv;
        using var http = new HttpClient { BaseAddress = new Uri("https://api.smartthings.com/v1/"), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", tv.SmartThingsToken.Trim());

        if (string.IsNullOrWhiteSpace(tv.SmartThingsDeviceId))
        {
            var resp = await http.GetAsync("devices");
            if (!resp.IsSuccessStatusCode) { Log.Write($"SmartThings: список устройств → {(int)resp.StatusCode} (ключ истёк или без прав Devices?)"); return false; }
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var d in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                string Prop(params string[] path)
                {
                    var e = d;
                    foreach (var p in path) if (e.ValueKind != System.Text.Json.JsonValueKind.Object || !e.TryGetProperty(p, out e)) return "";
                    return e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() ?? "" : "";
                }
                var label = Prop("label") + " " + Prop("name");
                bool isTv = Prop("ocf", "deviceType") == "oic.d.tv" || label.Contains("TV", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("телевизор", StringComparison.OrdinalIgnoreCase);
                if (!isTv) continue;
                tv.SmartThingsDeviceId = Prop("deviceId");
                Log.Write($"SmartThings: нашёл ТВ «{label.Trim()}» ({tv.SmartThingsDeviceId})");
                cfg.Save();
                break;
            }
            if (string.IsNullOrWhiteSpace(tv.SmartThingsDeviceId)) { Log.Write("SmartThings: телевизор в аккаунте не найден"); return false; }
        }

        var body = new StringContent("""{"commands":[{"component":"main","capability":"switch","command":"off"}]}""", Encoding.UTF8, "application/json");
        var r = await http.PostAsync($"devices/{tv.SmartThingsDeviceId}/commands", body);
        Log.Write($"SmartThings: выключить ТВ → {(int)r.StatusCode} {(r.IsSuccessStatusCode ? "OK" : await r.Content.ReadAsStringAsync())}");
        return r.IsSuccessStatusCode;
    }

    static async Task<bool> Tizen(string ip, bool secure)
    {
        var name = Convert.ToBase64String(Encoding.UTF8.GetBytes(AppName));
        var uri = new Uri(secure
            ? $"wss://{ip}:8002/api/v2/channels/samsung.remote.control?name={name}"
            : $"ws://{ip}:8001/api/v2/channels/samsung.remote.control?name={name}");
        using var ws = new ClientWebSocket();
        if (secure) ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await ws.ConnectAsync(uri, cts.Token);
        var buf = new byte[4096];
        await ws.ReceiveAsync(buf, cts.Token); // ms.channel.connect
        var cmd = """{"method":"ms.remote.control","params":{"Cmd":"Click","DataOfCmd":"KEY_POWER","Option":"false","TypeOfRemote":"SendRemoteKey"}}""";
        await ws.SendAsync(Encoding.UTF8.GetBytes(cmd), WebSocketMessageType.Text, true, cts.Token);
        await Task.Delay(500);
        return true;
    }

    static async Task<bool> Legacy(string ip)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        static byte[] Field(string s) { var b = Encoding.ASCII.GetBytes(s); return [(byte)b.Length, 0, .. b]; }
        static byte[] Packet(string app, byte[] msg) => [0, .. Field(app), (byte)msg.Length, 0, .. msg];

        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await tcp.ConnectAsync(ip, 55000, cts.Token);
        var s = tcp.GetStream();
        byte[] auth = [0x64, 0, .. Field(B64("127.0.0.1")), .. Field(B64("00-00-00-00-00-00")), .. Field(B64(AppName))];
        await s.WriteAsync(Packet("iphone..iapp.samsung", auth), cts.Token);
        byte[] key = [0, 0, 0, .. Field(B64("KEY_POWEROFF"))];
        await s.WriteAsync(Packet("iphone.UE40C6000.iapp.samsung", key), cts.Token);
        await Task.Delay(300);
        return true;
    }
}
