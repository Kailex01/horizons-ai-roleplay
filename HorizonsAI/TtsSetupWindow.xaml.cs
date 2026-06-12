using System.IO;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HorizonsAI;

public partial class TtsSetupWindow : Window
{
    private string _selectedModel = "multi-v1_0"; // default: best for roleplay
    private CancellationTokenSource? _cts;

    public bool Downloaded { get; private set; } = false;

    private const string EnUrl    = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2";
    private const string MultiV10Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2";
    private const string MultiV11Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_1.tar.bz2";

    public TtsSetupWindow() => InitializeComponent();

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    // ── Selection ──────────────────────────────────────────────────────────────

    private void SelectEn_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedModel = "en-v0_19";
        SetBorderSelected(EnBorder,  EnDot,  true);
        SetBorderSelected(V10Border, V10Dot, false);
        SetBorderSelected(V11Border, V11Dot, false);
    }

    private void SelectV10_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedModel = "multi-v1_0";
        SetBorderSelected(EnBorder,  EnDot,  false);
        SetBorderSelected(V10Border, V10Dot, true);
        SetBorderSelected(V11Border, V11Dot, false);
    }

    private void SelectV11_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedModel = "multi-v1_1";
        SetBorderSelected(EnBorder,  EnDot,  false);
        SetBorderSelected(V10Border, V10Dot, false);
        SetBorderSelected(V11Border, V11Dot, true);
    }

    private static void SetBorderSelected(System.Windows.Controls.Border border,
                                          System.Windows.Shapes.Ellipse  dot,
                                          bool selected)
    {
        border.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x20))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x50));
        border.Background = selected
            ? new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x2A))
            : new SolidColorBrush(Color.FromRgb(0x0A, 0x10, 0x18));
        dot.Fill = selected
            ? new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x20))
            : new SolidColorBrush(Colors.Transparent);
    }

    // ── Download ───────────────────────────────────────────────────────────────

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        DownloadBtn.IsEnabled = false;
        SkipBtn.IsEnabled     = false;
        ProgressPanel.Visibility = Visibility.Visible;

        _cts = new CancellationTokenSource();
        var url = _selectedModel switch
        {
            "multi-v1_0" => MultiV10Url,
            "multi-v1_1" => MultiV11Url,
            _            => EnUrl,
        };
        var modelType = _selectedModel;

        try
        {
            await DownloadAndExtractAsync(url, AppConfig.TtsFolder, modelType, _cts.Token);
            Downloaded   = true;
            DialogResult = true; // automatically closes the dialog
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text     = "Cancelled.";
            DownloadBtn.IsEnabled = true;
            SkipBtn.IsEnabled     = true;
        }
        catch (Exception ex)
        {
            ProgressText.Text     = $"Error: {ex.Message}";
            DownloadBtn.IsEnabled = true;
            SkipBtn.IsEnabled     = true;
        }
    }

    private async Task DownloadAndExtractAsync(string url, string destFolder,
                                                string modelType, CancellationToken ct)
    {
        Directory.CreateDirectory(destFolder);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total   = response.Content.Headers.ContentLength ?? -1L;
        var tmpDir  = Path.Combine(AppConfig.DataFolder, "temp");
        Directory.CreateDirectory(tmpDir);
        var tmpPath = Path.Combine(tmpDir, "kokoro_download.tar.bz2");

        // ── Download ───────────────────────────────────────────────────────────
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buf        = new byte[65536];
            long downloaded = 0;
            int  read;

            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;

                if (total > 0)
                {
                    var pct    = (int)(downloaded * 100L / total);
                    var mb     = downloaded / 1_048_576.0;
                    var totalM = total      / 1_048_576.0;
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value  = pct;
                        ProgressText.Text  = $"Downloading… {mb:F0} / {totalM:F0} MB ({pct}%)";
                    });
                }
            }
        }

        // ── Extract ────────────────────────────────────────────────────────────
        Dispatcher.Invoke(() =>
        {
            ProgressBar.IsIndeterminate = true;
            ProgressText.Text           = "Extracting archive… (this may take a few minutes)";
        });

        await Task.Run(() => ExtractTarBz2(tmpPath, destFolder), ct);

        Dispatcher.Invoke(() =>
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value           = 100;
            ProgressText.Text           = "Done.";
        });

        // Write model-type marker so KokoroService picks the right SID table
        await File.WriteAllTextAsync(Path.Combine(destFolder, "model_type.txt"), modelType, ct);

        try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
    }

    private static void ExtractTarBz2(string archivePath, string destFolder)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var reader     = ReaderFactory.Open(fileStream);

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;

            // Strip the top-level folder (e.g. "kokoro-en-v0_19/model.onnx" → "model.onnx")
            var parts = reader.Entry.Key?.Replace('\\', '/').Split('/') ?? [];
            var rel   = string.Join(Path.DirectorySeparatorChar.ToString(),
                                    parts.Length > 1 ? parts.Skip(1) : parts);
            if (string.IsNullOrEmpty(rel)) continue;

            var dest = Path.Combine(destFolder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            reader.WriteEntryToFile(dest, new ExtractionOptions { Overwrite = true });
        }
    }

    // ── Skip ───────────────────────────────────────────────────────────────────

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
