using System.Runtime.InteropServices;
using System.Text;

namespace Teleprompter.Services;

public sealed class ClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (TrySetTextOnce(text)) return true;
            if (attempt < 3) Thread.Sleep(5);
        }
        return false;
    }

    private static bool TrySetTextOnce(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (memory == IntPtr.Zero) return false;

        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }

            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero) return false;
                memory = IntPtr.Zero;
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (memory != IntPtr.Zero) GlobalFree(memory);
        }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr memory);
}
