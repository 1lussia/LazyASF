using System.Runtime.InteropServices;

namespace LazyASF.Native;

public sealed record AudioEndpoint(string Id, string Name, string DeviceName, uint State, bool IsRender)
{
    public bool Active => State == Audio.DEVICE_STATE_ACTIVE;
    public bool IsHandsFree => Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase)
                               || Name.Contains("Headset", StringComparison.OrdinalIgnoreCase)
                               || Name.Contains("гарнитур", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Core Audio: список устройств, устройство по умолчанию и «Подключить» для Bluetooth-наушников
/// (то же самое, что кнопка «Подключить» в параметрах звука — KSPROPERTY_ONESHOT_RECONNECT).
/// </summary>
public static class Audio
{
    public const uint DEVICE_STATE_ACTIVE = 1, DEVICE_STATEMASK_ALL = 0xF;
    const uint CLSCTX_ALL = 0x17;

    enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p, p2; }
    [StructLayout(LayoutKind.Sequential)] struct KSPROPERTY { public Guid Set; public uint Id; public uint Flags; }

    static readonly PROPERTYKEY PKEY_Device_FriendlyName = new() { fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
    static readonly PROPERTYKEY PKEY_DeviceInterface_FriendlyName = new() { fmtid = new("026e516e-b814-414b-83cd-856d6fef4822"), pid = 2 };
    static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint KSPROPERTY_ONESHOT_RECONNECT = 0, KSPROPERTY_ONESHOT_DISCONNECT = 1, KSPROPERTY_TYPE_GET = 1;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IntPtr other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out int connected);
        [PreserveSig] int GetConnectedTo(out IntPtr other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KSPROPERTY prop, uint propLen, IntPtr data, uint dataLen, out uint returned);
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

    static string ReadString(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out var pv) != 0) return "";
        try { return pv.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.p) ?? "" : ""; }
        finally { PropVariantClear(ref pv); }
    }

    public static List<AudioEndpoint> List(bool renderOnly = true)
    {
        var result = new List<AudioEndpoint>();
        var en = Enumerator();
        foreach (var flow in renderOnly ? new[] { EDataFlow.Render } : new[] { EDataFlow.Render, EDataFlow.Capture })
        {
            if (en.EnumAudioEndpoints(flow, DEVICE_STATEMASK_ALL, out var col) != 0) continue;
            col.GetCount(out var n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0) continue;
                dev.GetId(out var id);
                dev.GetState(out var state);
                string name = "", devName = "";
                if (dev.OpenPropertyStore(0, out var store) == 0)
                {
                    name = ReadString(store, PKEY_Device_FriendlyName);
                    devName = ReadString(store, PKEY_DeviceInterface_FriendlyName);
                }
                result.Add(new AudioEndpoint(id, name, devName, state, flow == EDataFlow.Render));
            }
        }
        return result;
    }

    public static string? DefaultRenderId()
    {
        return Enumerator().GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var dev) == 0 && dev.GetId(out var id) == 0
            ? id : null;
    }

    public static bool SetDefault(string endpointId)
    {
        var pc = (IPolicyConfig)new PolicyConfigCom();
        bool ok = true;
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            ok &= pc.SetDefaultEndpoint(endpointId, role) == 0;
        return ok;
    }

    /// <summary>Наушники по подстроке имени. Сначала стерео (A2DP), потом Hands-Free.</summary>
    public static List<AudioEndpoint> FindHeadphones(string nameFragment) =>
        List().Where(e => e.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)
                       || e.DeviceName.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
              .OrderBy(e => e.IsHandsFree).ToList();

    /// <summary>Попросить Windows подключить / отключить Bluetooth-аудиоустройство.</summary>
    public static bool BtOneShot(AudioEndpoint ep, bool connect)
    {
        try
        {
            var en = Enumerator();
            if (en.GetDevice(ep.Id, out var dev) != 0) return false;
            var iidTopo = typeof(IDeviceTopology).GUID;
            if (dev.Activate(ref iidTopo, CLSCTX_ALL, IntPtr.Zero, out var topoObj) != 0) return false;
            var topo = (IDeviceTopology)topoObj;
            if (topo.GetConnector(0, out var conn) != 0) return false;
            if (conn.GetDeviceIdConnectedTo(out var filterId) != 0) return false;
            if (en.GetDevice(filterId, out var filter) != 0) return false;
            var iidKs = typeof(IKsControl).GUID;
            if (filter.Activate(ref iidKs, CLSCTX_ALL, IntPtr.Zero, out var ksObj) != 0) return false;
            var ks = (IKsControl)ksObj;
            var prop = new KSPROPERTY
            {
                Set = KSPROPSETID_BtAudio,
                Id = connect ? KSPROPERTY_ONESHOT_RECONNECT : KSPROPERTY_ONESHOT_DISCONNECT,
                Flags = KSPROPERTY_TYPE_GET
            };
            int hr = ks.KsProperty(ref prop, (uint)Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
            if (hr != 0) Log.Write($"KsProperty({(connect ? "connect" : "disconnect")}) для «{ep.Name}»: 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception e)
        {
            Log.Write($"BtOneShot «{ep.Name}»: {e.Message}");
            return false;
        }
    }
}
