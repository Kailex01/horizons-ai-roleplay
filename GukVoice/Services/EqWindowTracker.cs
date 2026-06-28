using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GukVoice.Services;

// Polls the first (earliest-started) eqgame.exe process every 500ms.
// Uses GetClientRect + ClientToScreen so the rect covers only the game
// rendering area — no title bar, no window borders.
public sealed class EqWindowTracker : IDisposable
{
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private readonly System.Timers.Timer _timer;
    private int    _eqPid = 0;
    private IntPtr _hwnd  = IntPtr.Zero;

    public event Action<Rect>? RectChanged;
    public Rect CurrentRect { get; private set; }

    public EqWindowTracker()
    {
        _timer = new System.Timers.Timer(500) { AutoReset = true };
        _timer.Elapsed += (_, _) => Poll();
    }

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    private void Poll()
    {
        Process? proc = null;
        try
        {
            proc = Process.GetProcessesByName("eqgame")
                .OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } })
                .FirstOrDefault();
        }
        catch { return; }

        if (proc == null)
        {
            _hwnd  = IntPtr.Zero;
            _eqPid = 0;
            return;
        }

        if (proc.Id != _eqPid || !IsWindow(_hwnd))
        {
            _eqPid = proc.Id;
            try   { _hwnd = proc.MainWindowHandle; }
            catch { _hwnd = IntPtr.Zero; return;   }
        }

        if (_hwnd == IntPtr.Zero) return;

        // Client rect gives only the inner game area (no title bar / borders)
        if (!GetClientRect(_hwnd, out var client)) return;
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(_hwnd, ref origin)) return;

        var newRect = new Rect(
            origin.X, origin.Y,
            Math.Max(1, client.Right),
            Math.Max(1, client.Bottom));

        if (newRect == CurrentRect) return;
        CurrentRect = newRect;
        RectChanged?.Invoke(newRect);
    }

    public void Dispose() => _timer.Dispose();
}
