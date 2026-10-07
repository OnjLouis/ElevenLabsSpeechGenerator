using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class ProgramUpdater
    {
        private static readonly string[] ProgramFiles = { "ElevenLabsSpeechGenerator.exe", "Manual.html", "LICENSE.txt", "NAudio.dll", "ThirdPartyNotices.txt" };

        public static bool IsApplyUpdateCommand(string[] args)
        {
            return args.Any(value => value.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));
        }

        public static void ApplyUpdateFromCommandLine(string[] args)
        {
            var options = ParseOptions(args);
            var target = Required(options, "--update-target");
            var updaterRoot = Application.StartupPath;
            var rollback = Path.Combine(updaterRoot, "rollback");
            var rollbackNeeded = false;
            try
            {
                var waitPid = int.Parse(Required(options, "--update-wait-pid"));
                WaitForProcess(waitPid);
                var targetRoot = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
                if (!Directory.Exists(targetRoot)) throw new InvalidOperationException("The application folder no longer exists.");

                var packagePath = Path.Combine(updaterRoot, "update.zip");
                var signaturePath = Path.Combine(updaterRoot, "update.zip.sig");
                DownloadFile(Required(options, "--update-url"), packagePath);
                DownloadFile(Required(options, "--signature-url"), signaturePath);
                if (!UpdateService.VerifyPackageSignature(packagePath, signaturePath)) throw new InvalidOperationException("The update signature is missing or invalid. No files were changed.");

                var stage = Path.Combine(updaterRoot, "stage");
                Directory.CreateDirectory(stage);
                ExtractZipSafely(packagePath, stage);
                ValidateStagedPackage(stage, Required(options, "--update-version"));
                Directory.CreateDirectory(rollback);
                foreach (var name in ProgramFiles)
                {
                    var existing = Path.Combine(targetRoot, name);
                    if (File.Exists(existing)) File.Copy(existing, Path.Combine(rollback, name), true);
                }
                if (CurlTransport.UseForCurrentSystem)
                {
                    var existingTransport = Path.Combine(targetRoot, "Win7Transport");
                    if (!Directory.Exists(existingTransport)) throw new InvalidDataException("The existing Windows 7 TLS transport is missing.");
                    CopyDirectory(existingTransport, Path.Combine(rollback, "Win7Transport"));
                }
                SaveUpdateBackup(targetRoot, rollback);
                rollbackNeeded = true;
                foreach (var name in ProgramFiles) File.Copy(Path.Combine(stage, name), Path.Combine(targetRoot, name), true);
                if (CurlTransport.UseForCurrentSystem)
                {
                    var targetTransport = Path.Combine(targetRoot, "Win7Transport");
                    RejectReparsePoint(targetTransport);
                    Directory.Delete(targetTransport, true);
                    CopyDirectory(Path.Combine(stage, "Win7Transport"), targetTransport);
                }

                var targetExe = Path.Combine(targetRoot, ProgramFiles[0]);
#if PRIVATE_TEST
                if (SuppressRestartForTest) return;
#endif
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = "--cleanup-update " + Quote(updaterRoot),
                    WorkingDirectory = targetRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (rollbackNeeded)
                {
                    try { RestoreRollback(target, rollback); }
                    catch (Exception restoreError) { AppLog.WriteException("Update rollback failed", restoreError); }
                }
                WriteUpdateError(target, ex);
#if PRIVATE_TEST
                if (SuppressRestartForTest) throw;
#endif
                MessageBox.Show("The update could not be installed." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "The previous program files were retained or restored.", "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

#if PRIVATE_TEST
        internal static bool SuppressRestartForTest;
#endif

        public static void ScheduleCleanupFromCommandLine(string[] args)
        {
            var options = ParseOptions(args);
            string requested;
            if (!options.TryGetValue("--cleanup-update", out requested) || string.IsNullOrWhiteSpace(requested)) return;
            var updateBase = Path.GetFullPath(Path.Combine(AppPaths.UserFolder, "Update Temp")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(updateBase, StringComparison.OrdinalIgnoreCase)) return;
            Task.Run(delegate
            {
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(candidate)) Directory.Delete(candidate, true);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(500);
                    }
                }
                AppLog.Write("Updater staging could not be removed and will be retried after a future update.");
            });
        }

        private static void WaitForProcess(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (!process.WaitForExit(30000)) throw new TimeoutException("The previous application process did not exit within 30 seconds.");
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private static void DownloadFile(string url, string destination)
        {
            if (CurlTransport.UseForCurrentSystem)
            {
                CurlTransport.DownloadFile(url, destination);
                return;
            }
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "ElevenLabs Speech Generator updater";
                client.DownloadFile(url, destination);
            }
        }

        private static void ExtractZipSafely(string zipPath, string destination)
        {
            var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update contains an unsafe path.");
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }

        private static void ValidateStagedPackage(string stage, string expectedVersion)
        {
            foreach (var name in ProgramFiles)
            {
                if (!File.Exists(Path.Combine(stage, name))) throw new InvalidDataException("The signed package is missing " + name + ".");
            }
            if (CurlTransport.UseForCurrentSystem)
            {
                foreach (var name in new[] { "curl.exe", "curl-ca-bundle.crt", "COPYING.txt" })
                {
                    if (!File.Exists(Path.Combine(stage, Path.Combine("Win7Transport", name))))
                        throw new InvalidDataException("The signed Windows 7 package is missing " + name + ".");
                }
            }
            Version expected;
            Version actual;
            var versionText = FileVersionInfo.GetVersionInfo(Path.Combine(stage, ProgramFiles[0])).FileVersion;
            if (!Version.TryParse(expectedVersion, out expected) || !Version.TryParse(versionText, out actual) || actual.Major != expected.Major || actual.Minor != expected.Minor || actual.Build != expected.Build)
            {
                throw new InvalidDataException("The signed package version does not match the release version.");
            }
        }

        private static void SaveUpdateBackup(string targetRoot, string rollback)
        {
            var backups = Path.Combine(targetRoot, Path.Combine("User", "Backups"));
            Directory.CreateDirectory(backups);
            var archivePath = Path.Combine(backups, "Update-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var name in ProgramFiles)
                {
                    var path = Path.Combine(rollback, name);
                    if (File.Exists(path)) archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                }
                var transport = Path.Combine(rollback, "Win7Transport");
                if (Directory.Exists(transport))
                {
                    foreach (var path in Directory.GetFiles(transport, "*", SearchOption.AllDirectories))
                    {
                        var entry = "Win7Transport/" + path.Substring(transport.Length + 1).Replace('\\', '/');
                        archive.CreateEntryFromFile(path, entry, CompressionLevel.Optimal);
                    }
                }
            }
            foreach (var old in new DirectoryInfo(backups).GetFiles("Update-before-*.zip").OrderByDescending(file => file.CreationTimeUtc).Skip(3))
            {
                try { old.Delete(); } catch { }
            }
        }

        private static void RestoreRollback(string targetRoot, string rollback)
        {
            var fullTarget = Path.GetFullPath(targetRoot);
            foreach (var name in ProgramFiles)
            {
                var source = Path.Combine(rollback, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(fullTarget, name), true);
                else if (File.Exists(Path.Combine(fullTarget, name))) File.Delete(Path.Combine(fullTarget, name));
            }
            var previousTransport = Path.Combine(rollback, "Win7Transport");
            if (Directory.Exists(previousTransport))
            {
                var installedTransport = Path.Combine(fullTarget, "Win7Transport");
                if (Directory.Exists(installedTransport))
                {
                    RejectReparsePoint(installedTransport);
                    Directory.Delete(installedTransport, true);
                }
                CopyDirectory(previousTransport, installedTransport);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            RejectReparsePoint(source);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                RejectReparsePoint(file);
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }
            foreach (var folder in Directory.GetDirectories(source))
                CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Windows 7 TLS transport contains a filesystem link.");
        }

        private static void WriteUpdateError(string targetRoot, Exception exception)
        {
            try
            {
                var folder = Directory.Exists(targetRoot) ? Path.Combine(targetRoot, Path.Combine("User", "Logs")) : Application.StartupPath;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "Update-error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + exception + Environment.NewLine);
            }
            catch { }
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal)) continue;
                var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[++index] : string.Empty;
                values[args[index - (value.Length == 0 ? 0 : 1)]] = value;
            }
            return values;
        }

        private static string Required(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing updater argument: " + key);
            return value;
        }

        private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }
    }
}
