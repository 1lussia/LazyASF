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

    public static async Task<bool> PowerOff(TvConfig tv)
    {
        if (string.IsNullOrWhiteSpace(tv.Ip)) { Log.Write("ТВ: IP не задан (tv.ip в config.json) — пропускаю"); return false; }
        foreach (var attempt in new Func<string, Task<bool>>[] { ip => Tizen(ip, false), ip => Tizen(ip, true), Legacy })
        {
            try { if (await attempt(tv.Ip)) { Log.Write("ТВ: команда выключения отправлена"); return true; } }
            catch (Exception e) { Log.Write($"ТВ: {e.GetType().Name}: {e.Message}"); }
        }
        return false;
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
