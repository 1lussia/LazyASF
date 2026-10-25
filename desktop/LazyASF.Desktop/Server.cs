using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LazyASF.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LazyASF;

/// <summary>
/// WebSocket-сервер для телефона (ws://ip:47800/ws) + UDP-обнаружение в локальной сети.
/// Протокол — JSON-сообщения с полем "t", описание в docs/PROTOCOL.md.
/// </summary>
public sealed class Server
{
    sealed class Client
    {
        public required WebSocket Ws;
        public string Device = "телефон";
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public double AccX, AccY, AccWheel, AccHWheel;
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly AppConfig _cfg;
    readonly Orchestrator _orch;
    readonly ConcurrentDictionary<Client, byte> _clients = new();
    WebApplication? _app;

    public string Pin { get; } = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
    public IReadOnlyCollection<string> ConnectedDevices => _clients.Keys.Select(c => c.Device).ToList();
    public event Action? ClientsChanged;

    public Server(AppConfig cfg, Orchestrator orch)
    {
        _cfg = cfg;
        _orch = orch;
        _orch.Changed += () => _ = Broadcast(StateMessage());
    }

    public static List<string> LocalIPs() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString()).Distinct().ToList();

    /// <summary>Наш адрес в той же подсети, что и <paramref name="remote"/> (например, 192.168.1.8 для 192.168.1.16).</summary>
    static IPAddress? LocalAddressFor(IPAddress remote)
    {
        var rb = remote.MapToIPv4().GetAddressBytes();
        foreach (var a in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up)
                     .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                     .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
        {
            var lb = a.Address.GetAddressBytes();
            var mb = a.IPv4Mask.GetAddressBytes();
            if (mb.All(b => b == 255)) continue; // /32 — это VPN-туннель
            bool same = true;
            for (int i = 0; i < 4; i++) same &= (lb[i] & mb[i]) == (rb[i] & mb[i]);
            if (same) return a.Address;
        }
        return null;
    }

    public async Task Start()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(_cfg.Port));
        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
        _app.MapGet("/", () => $"LazyASF on {Environment.MachineName}");
        _app.Map("/ws", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await HandleClient(ws, ctx.Connection.RemoteIpAddress);
        });
        await _app.StartAsync();
        Log.Write($"Сервер слушает порт {_cfg.Port}. Адреса: {string.Join(", ", LocalIPs())}. Код для телефона: {Pin}");
        _ = Task.Run(DiscoveryLoop);
    }

    public async Task Stop()
    {
        if (_app is not null) await _app.StopAsync();
    }

    // ---------------------------------------------------------------- discovery
    async Task DiscoveryLoop()
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, _cfg.DiscoveryPort)) { EnableBroadcast = true };
            var reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { app = "LazyASF", name = Environment.MachineName, port = _cfg.Port }));
            while (true)
            {
                var r = await udp.ReceiveAsync();
                if (!Encoding.UTF8.GetString(r.Buffer).StartsWith("LAZYASF?")) continue;
                // Отвечаем с адреса той же локальной сети: иначе при включённом VPN
                // Windows отправит ответ в туннель, и телефон его не получит.
                var local = LocalAddressFor(r.RemoteEndPoint.Address);
                if (local is null) { await udp.SendAsync(reply, r.RemoteEndPoint); continue; }
                using var direct = new UdpClient(new IPEndPoint(local, 0));
                await direct.SendAsync(reply, r.RemoteEndPoint);
            }
        }
        catch (Exception e) { Log.Write($"Обнаружение по UDP не работает: {e.Message}"); }
    }

    // ---------------------------------------------------------------- clients
    async Task HandleClient(WebSocket ws, IPAddress? ip)
    {
        var client = new Client { Ws = ws };
        var buf = new byte[64 * 1024];
        bool authed = false;
        string reason = "телефон закрыл соединение";
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var msg = await ReceiveText(ws, buf);
                if (msg is null) break;
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                var t = root.TryGetProperty("t", out var tp) ? tp.GetString() : null;

                if (!authed)
                {
                    authed = await Authenticate(client, t, root);
                    if (!authed) { await Send(client, new { t = "auth_failed" }); break; }
                    _clients[client] = 1;
                    Log.Write($"📱 Подключился: {client.Device} ({ip})");
                    ClientsChanged?.Invoke();
                    await Send(client, StateMessage());
                    await Send(client, DevicesMessage());
                    continue;
                }
                Handle(client, t, root);
            }
        }
        catch (Exception e) when (e is WebSocketException or JsonException or OperationCanceledException or IOException)
        {
            reason = e.InnerException?.Message ?? e.Message;
        }
        finally
        {
            if (ws.State == WebSocketState.CloseReceived)
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
            if (_clients.TryRemove(client, out _))
            {
                Log.Write($"📱 Отключился: {client.Device} ({reason})");
                ClientsChanged?.Invoke();
            }
        }
    }

    async Task<bool> Authenticate(Client client, string? t, JsonElement root)
    {
        client.Device = root.TryGetProperty("device", out var d) ? d.GetString() ?? "телефон" : "телефон";
        if (t == "hello" && root.TryGetProperty("token", out var tok) && _cfg.PairedTokens.Contains(tok.GetString() ?? ""))
        {
            await Send(client, new { t = "welcome", name = Environment.MachineName });
            return true;
        }
        if (t == "pair" && root.TryGetProperty("pin", out var pin) && pin.GetString() == Pin)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            lock (_cfg) { _cfg.PairedTokens.Add(token); _cfg.Save(); }
            await Send(client, new { t = "paired", token, name = Environment.MachineName });
            Log.Write($"Новый телефон сопряжён: {client.Device}");
            return true;
        }
        return false;
    }

    /// <summary>Фоновая задача, ошибки которой попадают в журнал, а не пропадают.</summary>
    static void Background(Func<Task> work) => _ = Task.Run(async () =>
    {
        try { await work(); }
        catch (Exception e) { Log.Write($"Ошибка: {e}"); }
    });

    static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    void Handle(Client c, string? t, JsonElement m)
    {
        switch (t)
        {
            case "move":
                c.AccX += Num(m, "dx"); c.AccY += Num(m, "dy");
                int ix = (int)c.AccX, iy = (int)c.AccY;
                c.AccX -= ix; c.AccY -= iy;
                Input.MoveBy(ix, iy);
                break;
            case "scroll":
                c.AccWheel += Num(m, "dy"); c.AccHWheel += Num(m, "dx");
                int w = (int)c.AccWheel, hw = (int)c.AccHWheel;
                c.AccWheel -= w; c.AccHWheel -= hw;
                Input.Wheel(w, hw);
                break;
            case "btn":
                Input.Button(Str(m, "b"), m.TryGetProperty("down", out var down) && down.GetBoolean());
                break;
            case "click":
                Input.Click(Str(m, "b") is { Length: > 0 } b ? b : "left");
                break;
            case "key":
                Input.Combo(Str(m, "k"));
                break;
            case "text":
                Input.Text(Str(m, "s"));
                break;
            case "launch":
            {
                // Поля читаем здесь: JsonDocument освобождается сразу после Handle,
                // а Task.Run выполнится позже — чтение внутри него падало молча.
                string site = Str(m, "site"), hp = Str(m, "headphones"), output = Str(m, "output");
                Background(() => _orch.Launch(site, hp, output));
                break;
            }
            case "headphones_connect":
            {
                string hp = Str(m, "headphones");
                Background(() => _orch.SwitchOutput("headphones", hp));
                break;
            }
            case "audio_output":
            {
                string target = Str(m, "target"), hp = Str(m, "headphones");
                Background(() => _orch.SwitchOutput(target, hp));
                break;
            }
            case "restore":
                Background(() => _orch.Restore("с телефона"));
                break;
            case "sleep":
                _orch.SetSleep((int)Num(m, "minutes"));
                break;
            case "sleep_cancel":
                _orch.CancelSleep();
                break;
            case "devices":
                _ = Send(c, DevicesMessage());
                break;
            case "ping":
                _ = Send(c, new { t = "pong" });
                break;
        }
    }

    object StateMessage() => new { t = "state", state = _orch.Snapshot() };

    object DevicesMessage()
    {
        var names = Bluetooth.Paired().Where(d => d.IsAudio).Select(d => d.Name).ToList();
        if (!names.Contains(_cfg.DefaultHeadphones)) names.Insert(0, _cfg.DefaultHeadphones);
        return new { t = "devices", devices = names, @default = _cfg.DefaultHeadphones };
    }

    static async Task<string?> ReceiveText(WebSocket ws, byte[] buf)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    async Task Send(Client c, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        await c.SendLock.WaitAsync();
        try
        {
            if (c.Ws.State == WebSocketState.Open)
                await c.Ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
        finally { c.SendLock.Release(); }
    }

    Task Broadcast(object payload) => Task.WhenAll(_clients.Keys.Select(c => Send(c, payload)));
}
