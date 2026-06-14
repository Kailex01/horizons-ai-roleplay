using System.Text;
using System.Timers;

namespace GukVoice.Services;

// Tails an EQ log file and emits complete lines as they are appended.
// Uses timer-based polling (FileSystemWatcher misses events under heavy write load).
public sealed class EqLogWatcher : IDisposable
{
    private readonly string  _path;
    private FileStream?      _stream;
    private StreamReader?    _reader;
    private readonly System.Timers.Timer _timer;
    private int        _pollInProgress;  // Interlocked — prevents concurrent Poll() calls
    private bool       _waitingForFile;  // true when we deleted the log and are waiting for EQ to recreate it
    private Func<bool>? _isEqRunning;

    public event Action<string>? LineReceived;
    public event Action<string>? Error;
    public event Action?         FileFound;   // fires when the polled-for log file reappears

    public EqLogWatcher(string logPath)
    {
        _path  = logPath;
        _timer = new System.Timers.Timer(150) { AutoReset = true };
        _timer.Elapsed += (_, _) => Poll();
    }

    public void Start()
    {
        Stop(); // release any existing resources before re-opening
        try
        {
            _stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                                     FileShare.ReadWrite | FileShare.Delete);
            _stream.Seek(0, SeekOrigin.End);
            _reader = new StreamReader(_stream, Encoding.UTF8,
                                       detectEncodingFromByteOrderMarks: false,
                                       bufferSize: 4096, leaveOpen: true);
            _timer.Start();
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _waitingForFile = false;
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
    }

    // Call after deleting the log file. Keeps the timer running and watches for
    // EQ to recreate it. Only polls while isEqRunning() returns true.
    public void WaitForNewFile(Func<bool> isEqRunning)
    {
        _reader?.Dispose(); _reader = null;
        _stream?.Dispose(); _stream = null;
        _isEqRunning    = isEqRunning;
        _waitingForFile = true;
        Interlocked.Exchange(ref _pollInProgress, 0);
        _timer.Start();
    }

    private void Poll()
    {
        // System.Timers.Timer fires on the thread pool with AutoReset=true.
        // If a previous Poll() is still blocked in Dispatcher.Invoke, a second
        // tick would start concurrently and read the same StreamReader — causing
        // duplicate lines. The Interlocked flag ensures only one Poll() runs at a time.
        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0) return;
        try
        {
            if (_waitingForFile)
            {
                // Stop polling if EQ is no longer running
                if (_isEqRunning?.Invoke() == false)
                {
                    _timer.Stop();
                    _waitingForFile = false;
                    return;
                }

                // EQ has created a new log file — open from the beginning
                if (File.Exists(_path))
                {
                    _waitingForFile = false;
                    _stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                                             FileShare.ReadWrite | FileShare.Delete);
                    _reader = new StreamReader(_stream, Encoding.UTF8,
                                               detectEncodingFromByteOrderMarks: false,
                                               bufferSize: 4096, leaveOpen: true);
                    FileFound?.Invoke();
                }
                return;
            }

            if (_reader == null) return;
            string? line;
            while ((line = _reader.ReadLine()) != null)
                if (!string.IsNullOrWhiteSpace(line))
                    LineReceived?.Invoke(line);
        }
        catch { }
        finally { Interlocked.Exchange(ref _pollInProgress, 0); }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
    }
}
