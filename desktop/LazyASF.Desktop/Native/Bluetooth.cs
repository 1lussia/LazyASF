using System.Runtime.InteropServices;
using Windows.Devices.Radios;

namespace LazyASF.Native;

public sealed record BtDevice(string Name, ulong Address, uint ClassOfDevice, bool Connected)
{
    /// <summary>Major class 0x04 = Audio/Video.</summary>
    public bool IsAudio => ((ClassOfDevice >> 8) & 0x1F) == 0x04;
}

public static class Bluetooth
{
    // ---------- радиомодуль (вкл/выкл) ----------
    static async Task<Radio?> GetRadio()
    {
        try
        {
            await Radio.RequestAccessAsync();
            var radios = await Radio.GetRadiosAsync();
            return radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch (Exception e)
        {
            Log.Write($"Bluetooth-радио недоступно: {e.Message}");
            return null;
        }
    }

    public static async Task<bool?> IsOn()
    {
        var r = await GetRadio();
        return r is null ? null : r.State == RadioState.On;
    }

    public static async Task<bool> SetOn(bool on)
    {
        var r = await GetRadio();
        if (r is null) return false;
        if ((r.State == RadioState.On) == on) return true;
        var res = await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
        if (res != RadioAccessStatus.Allowed) Log.Write($"Bluetooth SetState({on}) → {res}");
        return res == RadioAccessStatus.Allowed;
    }

    // ---------- спаренные устройства (bthprops) ----------
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEMTIME { public ushort y, m, dow, d, h, min, s, ms; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected, fRemembered, fAuthenticated;
        public SYSTEMTIME stLastSeen, stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public int fReturnAuthenticated, fReturnRemembered, fReturnUnknown, fReturnConnected, fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BLUETOOTH_FIND_RADIO_PARAMS { public int dwSize; }

    [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS p, ref BLUETOOTH_DEVICE_INFO info);
    [DllImport("bthprops.cpl", SetLastError = true)] static extern bool BluetoothFindNextDevice(IntPtr h, ref BLUETOOTH_DEVICE_INFO info);
    [DllImport("bthprops.cpl")] static extern bool BluetoothFindDeviceClose(IntPtr h);
    [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr hRadio);
    [DllImport("bthprops.cpl")] static extern bool BluetoothFindRadioClose(IntPtr h);
    [DllImport("bthprops.cpl")] static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static BLUETOOTH_DEVICE_INFO NewInfo() => new() { dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), szName = "" };

    static List<BLUETOOTH_DEVICE_INFO> RawPaired()
    {
        var list = new List<BLUETOOTH_DEVICE_INFO>();
        var p = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1, fReturnRemembered = 1, fReturnConnected = 1, fReturnUnknown = 0, fIssueInquiry = 0,
        };
        var info = NewInfo();
        var h = BluetoothFindFirstDevice(ref p, ref info);
        if (h == IntPtr.Zero) return list;
        try
        {
            do { list.Add(info); info = NewInfo(); } while (BluetoothFindNextDevice(h, ref info));
        }
        finally { BluetoothFindDeviceClose(h); }
        return list;
    }

    public static List<BtDevice> Paired()
    {
        try
        {
            return RawPaired().Select(i => new BtDevice(i.szName, i.Address, i.ulClassofDevice, i.fConnected != 0))
                              .GroupBy(d => d.Address).Select(g => g.First()).ToList();
        }
        catch (Exception e)
        {
            Log.Write($"Список Bluetooth-устройств: {e.Message}");
            return new();
        }
    }

    static readonly Guid AudioSink = new("0000110B-0000-1000-8000-00805F9B34FB");

    /// <summary>
    /// Запасной вариант подключения: выключить и включить службу «Аудиоприёмник» у спаренного устройства —
    /// Windows при этом заново подключается к наушникам. Медленно (~5-10 с), но работает почти везде.
    /// </summary>
    public static bool ToggleAudioService(string nameFragment)
    {
        try
        {
            var dev = RawPaired().FirstOrDefault(d => d.szName.Contains(nameFragment, StringComparison.OrdinalIgnoreCase));
            if (dev.dwSize == 0) { Log.Write($"Не нашёл спаренное устройство «{nameFragment}»"); return false; }
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            var hFind = BluetoothFindFirstRadio(ref rp, out var hRadio);
            if (hFind == IntPtr.Zero) return false;
            BluetoothFindRadioClose(hFind);
            try
            {
                var svc = AudioSink;
                uint r1 = BluetoothSetServiceState(hRadio, ref dev, ref svc, 0); // DISABLE
                Thread.Sleep(1500);
                uint r2 = BluetoothSetServiceState(hRadio, ref dev, ref svc, 1); // ENABLE
                Log.Write($"Перезапуск аудиослужбы «{dev.szName}»: {r1}/{r2}");
                return r2 == 0;
            }
            finally { CloseHandle(hRadio); }
        }
        catch (Exception e)
        {
            Log.Write($"ToggleAudioService: {e.Message}");
            return false;
        }
    }
}
