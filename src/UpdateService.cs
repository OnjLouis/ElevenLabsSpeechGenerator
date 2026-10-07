using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class UpdateService
    {
        internal static Func<bool> CanInstall = () => true;
        public const string ProjectUrl = "https://github.com/OnjLouis/ElevenLabsSpeechGenerator";
        private const string UserAgent = "ElevenLabs Speech Generator updater";
        private const string PackageName = "ElevenLabsSpeechGenerator.zip";
        private const string Windows7PackageName = "ElevenLabsSpeechGenerator-Win7.zip";
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>w+cSsOFYdI0vo+gOZ235Y9cEVUpbbcc2nm34R6eONyXvdmDTbHONaa501GP2sM0w+f0OatlUkW3VY8KpMrHyI8DGYpMNlSqhGf7ctGD/47vVvfNXI+FeVZaENv3rxlWW2nPAiWU8yArrIPrmMSY2FwW2TsMP0dUiumQ2lmAowhDNWA7oXLZPCseetH/gfGxzigu7K6q/O4yv2vcAKbb+p5ofAVCVqfqdch+ZtNfue6RUFPKZ2dDuLcGrPMs4shbvHplyEriEg1Z/KOpLLeQpKwDHmZws2+LvFHsuSxb7wiw15hk6wrygCJ7xYVoA4JgBoATM3HQH5vRcauvDNPPc3z3EvtqMX0KRd6N9uwY4ktw87XtQ959eieE+gz1dtQBRM/jRjaemw2PrlAAOsKO73GlHyW0AnbRkSEb7qGI7aHizivIWu3ubcLmS/6VxnC2PS3kLKLLAZkp1G9g4JGYCVIJ1VrYGJyFJZXI/9Y5J63McgaQ70mHapUqzh6pTeP05</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static void CheckAutomatically(IWin32Window owner, AppSettings settings)
        {
            if (settings == null || !AutomaticCheckDue(settings)) return;
            CheckForUpdates(owner, settings, true);
        }

        public static async void CheckForUpdates(IWin32Window owner, AppSettings settings, bool automatic)
        {
            if (settings == null) return;
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            try { settings.Save(); } catch { }
            try
            {
                var releases = await Task.Run(delegate { return FetchReleases(); });
                var release = LatestVersionedRelease(releases);
                Version current;
                Version remote;
                if (!Version.TryParse(Program.Version, out current) || release == null || !Version.TryParse(NormalizeVersion(release.TagName), out remote))
                {
                    if (!automatic) MessageBox.Show(owner, "No published release channel is available yet.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (remote <= current)
                {
                    if (!automatic) MessageBox.Show(owner, Program.AppName + " is up to date. Current version: " + Program.Version + ".", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var package = FindPackage(release);
                if (automatic && settings.InstallUpdatesSilently && package != null && package.Signature != null)
                {
                    StartSelfUpdate(owner, package, remote.ToString(), true);
                    return;
                }
                ShowUpdateDialog(owner, release, releases, current, remote, package);
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Update check failed", ex);
                if (!automatic) MessageBox.Show(owner, "Could not check for updates." + Environment.NewLine + Environment.NewLine + ex.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        internal static bool VerifyPackageSignature(string zipPath, string signaturePath)
        {
#if PRIVATE_TEST
            if (!string.IsNullOrEmpty(TestPublicKeyXml)) return VerifyPackageSignatureWithKey(zipPath, signaturePath, TestPublicKeyXml);
#endif
            return VerifyPackageSignatureWithKey(zipPath, signaturePath, PublicKeyXml);
        }

#if PRIVATE_TEST
        internal static string TestPublicKeyXml;
#endif

        internal static bool VerifyPackageSignatureWithKey(string zipPath, string signaturePath, string publicKeyXml)
        {
            var signatureText = File.ReadAllText(signaturePath, Encoding.ASCII).Trim();
            byte[] signature;
            try { signature = Convert.FromBase64String(signatureText); }
            catch (FormatException) { return false; }
            using (var rsa = new RSACryptoServiceProvider())
            {
                try
                {
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(File.ReadAllBytes(zipPath), CryptoConfig.MapNameToOID("SHA256"), signature);
                }
                finally { rsa.PersistKeyInCsp = false; }
            }
        }

        private static bool AutomaticCheckDue(AppSettings settings)
        {
            var frequency = AppSettings.NormalizeUpdateFrequency(settings.UpdateCheckFrequency);
            if (frequency == "Never") return false;
            if (frequency == "Startup") return true;
            if (settings.LastUpdateCheckUtc == DateTime.MinValue) return true;
            var age = DateTime.UtcNow - settings.LastUpdateCheckUtc;
            return frequency == "Daily" ? age >= TimeSpan.FromDays(1) : age >= TimeSpan.FromDays(7);
        }

        private static void ShowUpdateDialog(IWin32Window owner, GitHubReleaseInfo release, IEnumerable<GitHubReleaseInfo> releases, Version current, Version remote, SignedPackage package)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Update available";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new System.Drawing.Size(720, 520);
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowIcon = false;
                dialog.ShowInTaskbar = false;
                dialog.AccessibleName = "Update available";
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(new Label { AutoSize = true, Text = Program.AppName + " " + remote + " is available." }, 0, 0);
                layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Release notes", Text = BuildReleaseNotes(releases, current, remote) }, 0, 1);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
                if (package != null && package.Signature != null)
                {
                    var install = new Button { Text = "&Download and install", AutoSize = true, AccessibleName = "Download and install verified update" };
                    install.Click += delegate { dialog.DialogResult = DialogResult.OK; dialog.Close(); StartSelfUpdate(owner, package, remote.ToString(), false); };
                    buttons.Controls.Add(install);
                    dialog.AcceptButton = install;
                }
                var releasePage = new Button { Text = "Open &release page", AutoSize = true };
                releasePage.Click += delegate { OpenUrl(string.IsNullOrWhiteSpace(release.HtmlUrl) ? ProjectUrl + "/releases" : release.HtmlUrl); };
                var later = new Button { Text = "&Later", AutoSize = true, DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(releasePage);
                buttons.Controls.Add(later);
                if (package == null || package.Signature == null) buttons.Controls.Add(new Label { AutoSize = true, Text = "Automatic installation is unavailable because the ZIP or signature is missing." });
                dialog.CancelButton = later;
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.ShowDialog(owner);
            }
        }

        private static void StartSelfUpdate(IWin32Window owner, SignedPackage package, string expectedVersion, bool silent)
        {
            if (!CanInstall()) { if (!silent) Ui.Result(owner, "Update postponed", "Finish the active request before installing an update."); return; }
            if (!silent && MessageBox.Show(owner,
                Program.AppName + " will close, verify the signed update, back up and replace only its program files, then restart. Portable settings and the complete User folder will be preserved." + Environment.NewLine + Environment.NewLine + "Continue?",
                "Download and install", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                var updaterRoot = Path.Combine(AppPaths.UserFolder, Path.Combine("Update Temp", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(updaterRoot);
                var updaterPath = Path.Combine(updaterRoot, "ElevenLabs Speech Generator Updater.exe");
                File.Copy(Application.ExecutablePath, updaterPath, true);
                File.Copy(Path.Combine(AppPaths.AppFolder, "NAudio.dll"), Path.Combine(updaterRoot, "NAudio.dll"));
                if (CurlTransport.UseForCurrentSystem)
                {
                    var source = Path.Combine(AppPaths.AppFolder, "Win7Transport");
                    var transport = Path.Combine(updaterRoot, "Win7Transport");
                    Directory.CreateDirectory(transport);
                    File.Copy(Path.Combine(source, "curl.exe"), Path.Combine(transport, "curl.exe"), true);
                    File.Copy(Path.Combine(source, "curl-ca-bundle.crt"), Path.Combine(transport, "curl-ca-bundle.crt"), true);
                }
                var arguments = "--apply-update --update-url " + Quote(package.Zip.BrowserDownloadUrl) +
                    " --signature-url " + Quote(package.Signature.BrowserDownloadUrl) +
                    " --update-version " + Quote(expectedVersion) +
                    " --update-target " + Quote(AppPaths.AppFolder) +
                    " --update-wait-pid " + Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo { FileName = updaterPath, Arguments = arguments, WorkingDirectory = updaterRoot, UseShellExecute = false, CreateNoWindow = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not start updater", ex);
                if (!silent) MessageBox.Show(owner, ex.Message, "Could not start updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static List<GitHubReleaseInfo> FetchReleases()
        {
            var url = ProjectUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases?per_page=100";
            var json = CurlTransport.UseForCurrentSystem ? CurlTransport.GetString(url) : DownloadString(url);
            var values = new JavaScriptSerializer().DeserializeObject(json) as object[];
                var releases = new List<GitHubReleaseInfo>();
                if (values == null) return releases;
                foreach (var value in values)
                {
                    var map = value as Dictionary<string, object>;
                    if (map == null) continue;
                    var release = new GitHubReleaseInfo
                    {
                        TagName = StringValue(map, "tag_name"), HtmlUrl = StringValue(map, "html_url"), Body = StringValue(map, "body"),
                        Draft = BoolValue(map, "draft"), Prerelease = BoolValue(map, "prerelease")
                    };
                    object assetValue;
                    var assets = map.TryGetValue("assets", out assetValue) ? assetValue as object[] : null;
                    if (assets != null)
                    {
                        foreach (var rawAsset in assets)
                        {
                            var assetMap = rawAsset as Dictionary<string, object>;
                            if (assetMap != null) release.Assets.Add(new GitHubReleaseAsset { Name = StringValue(assetMap, "name"), BrowserDownloadUrl = StringValue(assetMap, "browser_download_url") });
                        }
                    }
                    releases.Add(release);
                }
            return releases;
        }

        private static string DownloadString(string url)
        {
            using (var client = CreateClient()) return client.DownloadString(url);
        }

        private static GitHubReleaseInfo LatestVersionedRelease(IEnumerable<GitHubReleaseInfo> releases)
        {
            return releases.Select(item => new { Release = item, Version = ParseVersion(item.TagName) })
                .Where(item => item.Version != null && !item.Release.Draft && !item.Release.Prerelease)
                .OrderByDescending(item => item.Version).Select(item => item.Release).FirstOrDefault();
        }

        private static SignedPackage FindPackage(GitHubReleaseInfo release)
        {
            if (release == null) return null;
            var name = CurlTransport.UseForCurrentSystem ? Windows7PackageName : PackageName;
            var zip = release.Assets.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (zip == null) return null;
            var signature = release.Assets.FirstOrDefault(item => item.Name.Equals(name + ".sig", StringComparison.OrdinalIgnoreCase));
            return new SignedPackage { Zip = zip, Signature = signature };
        }

        private static string BuildReleaseNotes(IEnumerable<GitHubReleaseInfo> releases, Version current, Version latest)
        {
            var selected = releases.Select(item => new { Release = item, Version = ParseVersion(item.TagName) })
                .Where(item => item.Version != null && item.Version > current && item.Version <= latest && !item.Release.Draft && !item.Release.Prerelease)
                .OrderBy(item => item.Version).ToList();
            var builder = new StringBuilder();
            builder.AppendLine("Your version: " + current);
            builder.AppendLine("New version: " + latest);
            foreach (var item in selected)
            {
                builder.AppendLine();
                builder.AppendLine(item.Release.TagName);
                builder.AppendLine(PlainReleaseNotes(item.Release.Body));
            }
            return builder.ToString().TrimEnd();
        }

        private static WebClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            return client;
        }

        private static Version ParseVersion(string value)
        {
            Version version;
            return Version.TryParse(NormalizeVersion(value), out version) ? version : null;
        }

        private static string NormalizeVersion(string value) { return (value ?? string.Empty).Trim().TrimStart('v', 'V'); }

        private static string PlainReleaseNotes(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "No release notes were provided.";
            return string.Join(Environment.NewLine, markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                .Select(line => line.TrimEnd()).Select(line => line.StartsWith("#", StringComparison.Ordinal) ? line.TrimStart('#').Trim() : line)
                .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? "  " + line.Substring(2) : line));
        }

        private static void OpenUrl(string url) { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        private static string StringValue(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty; }
        private static bool BoolValue(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) && value is bool && (bool)value; }
        private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }

        private sealed class SignedPackage { public GitHubReleaseAsset Zip { get; set; } public GitHubReleaseAsset Signature { get; set; } }
        private sealed class GitHubReleaseInfo
        {
            public string TagName { get; set; } public string HtmlUrl { get; set; } public string Body { get; set; }
            public bool Draft { get; set; } public bool Prerelease { get; set; }
            public List<GitHubReleaseAsset> Assets { get; private set; }
            public GitHubReleaseInfo() { Assets = new List<GitHubReleaseAsset>(); }
        }
        private sealed class GitHubReleaseAsset { public string Name { get; set; } public string BrowserDownloadUrl { get; set; } }
    }
}
