using System.ComponentModel;
using System.Runtime.InteropServices;
using KeepingYouAwake.Core;
using Microsoft.Win32.SafeHandles;

namespace KeepingYouAwake.App;

internal sealed class WindowsPower : IPowerController
{
    private const uint Continuous = 0x80000000, SystemRequired = 1, DisplayRequired = 2;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private uint? previous;
    public bool Simulation { get; }
    public WindowsPower(bool simulation) => Simulation = simulation;
    public void SetAwake(bool awake, bool keepScreenOn)
    {
        AssertThread();
        var flags = Continuous | (awake ? SystemRequired : 0) | (awake && keepScreenOn ? DisplayRequired : 0);
        if (flags == previous) return;
        if (!Simulation && SetThreadExecutionState(flags) == 0) throw new Win32Exception("Windows 未能设置防休眠请求。");
        previous = flags;
    }
    public void RequestShutdown()
    {
        AssertThread();
        if (Simulation) return;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var desired = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, ref desired, Marshal.SizeOf<TokenPrivileges>(), out var old, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (Marshal.GetLastWin32Error() == 1300) throw new Win32Exception("当前账户没有关机权限，请检查 Windows 账户策略。");
            try
            {
                // No shutdown.exe /t: a positive /t silently implies /f.
                if (!InitiateSystemShutdownExW(null, null, 0, false, false, 0x80040000)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { AdjustTokenPrivileges(token, false, ref old, Marshal.SizeOf<TokenPrivileges>(), out _, out _); }
        }
    }
    public void Dispose() => SetAwake(false, false);
    private void AssertThread()
    { if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("电源请求必须由后台主线程处理。"); }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, int bufferLength, out TokenPrivileges previous, out uint returnLength);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitiateSystemShutdownExW(string? machine, string? message, uint timeout, [MarshalAs(UnmanagedType.Bool)] bool force, [MarshalAs(UnmanagedType.Bool)] bool reboot, uint reason);
}
