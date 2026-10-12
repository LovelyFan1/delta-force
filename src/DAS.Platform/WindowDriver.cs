using System.Runtime.InteropServices;

namespace DAS.Platform;

/// <summary>句柄绑定 + 消息投递。所有操作先校验句柄归属，修复并发串号。</summary>
public sealed class WindowDriver : IDisposable
{
    private readonly nint _root;
    private readonly int _ownerPid;

    public WindowDriver(nint root, int ownerPid) { _root = root; _ownerPid = ownerPid; }

    [DllImport("user32.dll")] private static extern bool PostMessage(nint hWnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern nint FindWindowEx(nint parent, nint child, string? cls, string? title);

    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102;

    public bool BelongsToOwner(nint hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var pid);
        return pid == _ownerPid;
    }

    public bool Type(nint hWnd, string text)
    {
        if (!BelongsToOwner(hWnd)) return false;           // 归属校验：不属于本账号的句柄一律拒绝
        foreach (var ch in text)
            if (!PostMessage(hWnd, WM_CHAR, ch, 0)) return false;
        return true;
    }

    public bool Press(nint hWnd, int vk)
    {
        if (!BelongsToOwner(hWnd)) return false;
        return PostMessage(hWnd, WM_KEYDOWN, vk, 0) && PostMessage(hWnd, WM_KEYUP, vk, 0);
    }

    public nint Child(string cls, string? title = null) => FindWindowEx(_root, 0, cls, title);

    public void Dispose() { /* 句柄由 ProcessGuard 统一回收 */ }
}
