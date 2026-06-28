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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private readonly System.Timers.Timer _timer;
    private int    _eqPid    = 0;
    private IntPtr _hwnd     = IntPtr.Zero;
    private bool   _diagFired = false;

    public event Action<Rect>?   RectChanged;
    // Fires once per EQ session with a multi-line diagnostics string
    public event Action<string>? DiagReady;
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
            _hwnd      = IntPtr.Zero;
            _eqPid     = 0;
            _diagFired = false;
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

        if (!_diagFired)
        {
            _diagFired = true;
            DiagReady?.Invoke(BuildDiag(client, origin));
        }

        if (newRect == CurrentRect) return;
        CurrentRect = newRect;
        RectChanged?.Invoke(newRect);
    }

    private string BuildDiag(RECT client, POINT clientOrigin)
    {
        GetWindowRect(_hwnd, out var wr);
        DwmGetWindowAttribute(_hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var dwm,
            Marshal.SizeOf<RECT>());
        uint dpi = GetDpiForWindow(_hwnd);

        return
            $"EQ window diagnostics (PID {_eqPid})\n" +
            $"  GetClientRect       : {client.Right} × {client.Bottom}\n" +
            $"  ClientToScreen (0,0): ({clientOrigin.X}, {clientOrigin.Y})\n" +
            $"  GetWindowRect       : pos=({wr.Left},{wr.Top})  size={wr.Right - wr.Left}×{wr.Bottom - wr.Top}\n" +
            $"  DwmExtFrameBounds   : pos=({dwm.Left},{dwm.Top})  size={dwm.Right - dwm.Left}×{dwm.Bottom - dwm.Top}\n" +
            $"  GetDpiForWindow     : {dpi}  (96=DPI-unaware, >96=DPI-aware)\n" +
            $"  Overlay (no DPI fix): Left={clientOrigin.X} Top={clientOrigin.Y} W={client.Right} H={client.Bottom} (WPF units)";
    }

    public void Dispose() => _timer.Dispose();
}
