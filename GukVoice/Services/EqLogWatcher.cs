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

    public event Action<string>? LineReceived;
    public event Action<string>? Error;

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
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
    }

    private void Poll()
    {
        if (_reader == null) return;
        try
        {
            string? line;
            while ((line = _reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    LineReceived?.Invoke(line);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
    }
}
