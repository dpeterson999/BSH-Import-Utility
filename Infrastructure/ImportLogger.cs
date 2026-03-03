using System;
using System.IO;
using System.Linq;

namespace BSH_Import_Utility.Infrastructure
{
    public static class ImportLogger
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "import_log.txt");

        private const int MaxLogSizeBytes = 1024 * 1024; // 1 MB

        public static void BeginSession(int fileCount)
        {
            TrimIfNeeded();
            File.AppendAllText(LogPath,
                $"{Environment.NewLine}" +
                $"========================================{Environment.NewLine}" +
                $"Import Session — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {fileCount} file(s){Environment.NewLine}" +
                $"========================================{Environment.NewLine}");
        }

        public static void Log(string message)
        {
            File.AppendAllText(LogPath,
                $"  {DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }

        public static void EndSession(int succeeded, int total)
        {
            File.AppendAllText(LogPath,
                $"  Result: {succeeded} of {total} imported successfully{Environment.NewLine}");
        }

        private static void TrimIfNeeded()
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSizeBytes)
                {
                    string[] lines = File.ReadAllLines(LogPath);
                    // Keep the most recent half
                    File.WriteAllLines(LogPath, lines.Skip(lines.Length / 2));
                }
            }
            catch { }
        }
    }
}