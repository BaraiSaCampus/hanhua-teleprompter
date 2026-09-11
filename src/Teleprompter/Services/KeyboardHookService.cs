using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Teleprompter.Services;

public enum PromptHotkey { Paste, Rollback }
public enum HookDecision { PassThrough, Swallow }

public sealed class KeyboardHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkC = 0x43;
    private const int VkV = 0x56;

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _cDown;
    private bool _vDown;

    public KeyboardHookService() => _callback = HookCallback;

    public Func<PromptHotkey, HookDecision>? HotkeyPressed { get; set; }
    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("无法启用全局快捷键。", new System.ComponentModel.Win32Exception());
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _cDown = false;
        _vDown = false;
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var message = wParam.ToInt32();
        var isDown = message is WmKeyDown or WmSysKeyDown;
        var isUp = message is WmKeyUp or WmSysKeyUp;
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var key = (int)data.VkCode;

        if (key == VkC && isUp) _cDown = false;
        if (key == VkV && isUp) _vDown = false;

        if (!IsControlDown() || IsOwnWindowForeground())
            return CallNextHookEx(_hook, code, wParam, lParam);

        if (key == VkC)
        {
            if (isDown && !_cDown)
            {
                _cDown = true;
                HotkeyPressed?.Invoke(PromptHotkey.Rollback);
            }
            return new IntPtr(1);
        }

        if (key == VkV)
        {
            if (isUp) return CallNextHookEx(_hook, code, wParam, lParam);
            if (isDown && _vDown) return new IntPtr(1);
            if (isDown)
            {
                _vDown = true;
                var decision = HotkeyPressed?.Invoke(PromptHotkey.Paste) ?? HookDecision.Swallow;
                return decision == HookDecision.Swallow ? new IntPtr(1) : CallNextHookEx(_hook, code, wParam, lParam);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsControlDown() => (GetAsyncKeyState(VkControl) & 0x8000) != 0;

    private static bool IsOwnWindowForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
