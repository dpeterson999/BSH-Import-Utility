using System;
using System.IO;
using System.Linq;
using File = System.IO.File;
using FileInfo = System.IO.FileInfo;

namespace BSH_Import_Utility.Infrastructure
{
    public static class ImportLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BSH Import Tool",
            "import_log.txt");

        private const int MaxLogSizeBytes = 1024 * 1024; // 1 MB

        private static void EnsureDirectoryExists()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        }

        public static void BeginSession(int fileCount)
        {
            EnsureDirectoryExists();
            TrimIfNeeded();
            File.AppendAllText(LogPath,
                $"{Environment.NewLine}" +
                $"========================================{Environment.NewLine}" +
                $"Import Session — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {fileCount} file(s){Environment.NewLine}" +
                $"========================================{Environment.NewLine}");
        }

        public static void Log(string message)
        {
            EnsureDirectoryExists();
            File.AppendAllText(LogPath,
                $"  {DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }

        public static void EndSession(int succeeded, int total)
        {
            EnsureDirectoryExists();
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
                    File.WriteAllLines(LogPath, lines.Skip(lines.Length / 2));
                }
            }
            catch { }
        }
    }
}