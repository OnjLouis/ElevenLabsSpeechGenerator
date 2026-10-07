using System;
using System.IO;
using System.Text;

namespace ElevenLabsSpeechGenerator
{
    internal static class AppLog
    {
        private const long MaximumLogBytes = 1024 * 1024;
        private static readonly object SyncRoot = new object();

        public static void Initialize()
        {
            Write("Application started.");
        }

        public static void Write(string message)
        {
            try
            {
                AppPaths.EnsureUserFolders();
                lock (SyncRoot)
                {
                    if (File.Exists(AppPaths.LogPath) && new FileInfo(AppPaths.LogPath).Length > MaximumLogBytes)
                    {
                        if (File.Exists(AppPaths.PreviousLogPath)) File.Delete(AppPaths.PreviousLogPath);
                        File.Move(AppPaths.LogPath, AppPaths.PreviousLogPath);
                    }
                    File.AppendAllText(AppPaths.LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Sanitize(message) + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        public static void WriteException(string context, Exception exception)
        {
            Write(context + ": " + (exception == null ? "Unknown error." : exception is ElevenLabsApiException ? exception.GetType().Name + " HTTP " + ((ElevenLabsApiException)exception).StatusCode : exception.ToString()));
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
