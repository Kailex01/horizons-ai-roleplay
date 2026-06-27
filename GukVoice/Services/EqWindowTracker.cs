using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GukVoice.Services;

// Polls the first (earliest-started) eqgame.exe process every 500ms.
// Always takes the first instance so a second boxing EQ window is ignored.
public sealed class EqWindowTracker : IDisposable
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly System.Timers.Timer _timer;
    private int    _eqPid  = 0;
    private IntPtr _hwnd   = IntPtr.Zero;
    private RECT   _lastRect;

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
        if (!GetWindowRect(_hwnd, out var r)) return;

        if (r.Left == _lastRect.Left && r.Top    == _lastRect.Top &&
            r.Right == _lastRect.Right && r.Bottom == _lastRect.Bottom)
            return;

        _lastRect = r;
        var wpf = new Rect(r.Left, r.Top,
            Math.Max(1, r.Right  - r.Left),
            Math.Max(1, r.Bottom - r.Top));
        CurrentRect = wpf;
        RectChanged?.Invoke(wpf);
    }

    public void Dispose() => _timer.Dispose();
}
