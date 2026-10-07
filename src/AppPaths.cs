using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal static class AppPaths
    {
        public static string AppFolder { get { return Application.StartupPath; } }
        public static string DefaultAudioFolder { get { return Path.Combine(AppFolder, "Audio"); } }
        public static string UserFolder { get { return Path.Combine(AppFolder, "User"); } }
        public static string DraftPath { get { return Path.Combine(UserFolder, "Drafts.json"); } }
        public static string SettingsPath { get { return Path.Combine(AppFolder, "ElevenLabsSpeechGenerator.ini"); } }
        public static string ApiKeyPath { get { return Path.Combine(UserFolder, "ApiKey." + Scope() + ".dat"); } }
        public static string ApiKeyLoadMessage { get; private set; }
        public static string LogFolder { get { return Path.Combine(UserFolder, "Logs"); } }
        public static string LogPath { get { return Path.Combine(LogFolder, "Latest.log"); } }
        public static string PreviousLogPath { get { return Path.Combine(LogFolder, "Previous.log"); } }
        public static string ManualPath { get { return Path.Combine(AppFolder, "Manual.html"); } }
        public static void EnsureUserFolders() { Directory.CreateDirectory(UserFolder); Directory.CreateDirectory(LogFolder); }
        public static string LoadApiKey()
        {
            ApiKeyLoadMessage = "";
            var path = File.Exists(ApiKeyPath) ? ApiKeyPath : Path.Combine(UserFolder, "ApiKey.dat");
            if (!File.Exists(path)) return "";
            try { return ApiKeyProtector.Unprotect(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { if (!(ex is CryptographicException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)) throw; ApiKeyLoadMessage = "The saved API key cannot be unlocked. Enter it again in Preferences."; return ""; }
        }
        public static void SaveApiKey(string key)
        {
            EnsureUserFolders(); key = (key ?? "").Trim();
            var copiedKey = Path.Combine(UserFolder, "ApiKey.dat");
            if (key.Length == 0) { if (File.Exists(ApiKeyPath)) File.Delete(ApiKeyPath); if (File.Exists(copiedKey)) File.Delete(copiedKey); return; }
            var protectedKey = ApiKeyProtector.Protect(key);
            if (ApiKeyProtector.Unprotect(protectedKey) != key) throw new CryptographicException("Could not verify protected API key.");
            FileNames.AtomicText(ApiKeyPath, protectedKey);
            if (ApiKeyProtector.Unprotect(File.ReadAllText(ApiKeyPath, Encoding.UTF8)) != key) throw new CryptographicException("Could not verify saved API key.");
            if (File.Exists(copiedKey)) File.Delete(copiedKey);
        }
        private static string Scope()
        {
            using (var identity = WindowsIdentity.GetCurrent()) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName + "\n" + identity.User.Value)), 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
