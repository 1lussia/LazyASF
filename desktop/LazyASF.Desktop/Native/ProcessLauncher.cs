using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LazyASF.Native;

/// <summary>
/// Запуск программы «от имени» другого процесса (обычно Проводника).
/// Нужно из-за раздельного туннелирования AmneziaVPN: LazyASF исключён из VPN, а драйвер
/// исключает и всех его «детей». Chrome, запущенный напрямую из LazyASF, шёл бы мимо VPN —
/// и YouTube / kino.watch не открывались. С родителем explorer.exe Chrome идёт через VPN, как обычно.
/// </summary>
public static class ProcessLauncher
{
    const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    const uint PROCESS_CREATE_PROCESS = 0x0080;
    static readonly IntPtr PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = (IntPtr)0x00020000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? cwd, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);

    /// <summary>true — запущено с родителем <paramref name="parentName"/>.</summary>
    public static bool StartAsChildOf(string parentName, string exe, string args)
    {
        int session = Process.GetCurrentProcess().SessionId;
        var parent = Process.GetProcessesByName(parentName).FirstOrDefault(p => p.SessionId == session);
        if (parent is null) return false;

        IntPtr hParent = OpenProcess(PROCESS_CREATE_PROCESS, false, parent.Id);
        if (hParent == IntPtr.Zero) return false;
        IntPtr list = IntPtr.Zero, value = IntPtr.Zero;
        try
        {
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            list = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) return false;

            value = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(value, hParent);
            if (!UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_PARENT_PROCESS, value, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                return false;

            var si = new STARTUPINFOEX { lpAttributeList = list };
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            var cmd = new StringBuilder($"\"{exe}\" {args}");
            if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out var pi))
                return false;
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
        finally
        {
            if (list != IntPtr.Zero) { DeleteProcThreadAttributeList(list); Marshal.FreeHGlobal(list); }
            if (value != IntPtr.Zero) Marshal.FreeHGlobal(value);
            CloseHandle(hParent);
        }
    }
}
