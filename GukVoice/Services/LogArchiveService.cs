using System.IO.Compression;

namespace GukVoice.Services;

public static class LogArchiveService
{
    public static void Archive(string logPath, string archiveFolder)
    {
        if (!File.Exists(logPath)) return;
        Directory.CreateDirectory(archiveFolder);

        var stamp       = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var archivePath = Path.Combine(archiveFolder, $"eqlog_{stamp}.log.gz");

        try
        {
            using (var input  = File.OpenRead(logPath))
            using (var output = File.Create(archivePath))
            using (var gzip   = new GZipStream(output, CompressionLevel.Optimal))
                input.CopyTo(gzip);

            // Streams are closed — safe to delete the original
            File.Delete(logPath);
        }
        catch { }
    }
}
