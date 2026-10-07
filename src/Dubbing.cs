using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class DubbingJob
    {
        public int SchemaVersion { get; set; }
        public string InputFile { get; set; }
        public string SourceUrl { get; set; }
        public bool UseUrl { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public string Name { get; set; }
        public string ProjectId { get; set; }
        public string LanguageId { get; set; }
        public string OutputFile { get; set; }
        public DubbingJob() { SchemaVersion = 1; InputFile = SourceUrl = SourceLanguage = Name = ProjectId = LanguageId = OutputFile = ""; TargetLanguage = "en"; }
        internal static bool ValidId(string id) { return id != null && Regex.IsMatch(id, "\\A[A-Za-z0-9_-]{1,128}\\z"); }
        public void Validate(bool creating)
        {
            if (SchemaVersion != 1 || new[] { InputFile, SourceUrl, SourceLanguage, TargetLanguage, Name, ProjectId, LanguageId, OutputFile }.Any(x => x == null)) throw new InvalidDataException("This is not a supported dubbing job.");
            if (!Regex.IsMatch(TargetLanguage, "\\A[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\\z") || SourceLanguage.Length > 0 && !Regex.IsMatch(SourceLanguage, "\\A[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\\z")) throw new InvalidDataException("Choose valid source and target languages.");
            if (Name.Length > 500) throw new InvalidDataException("Use a job name of no more than 500 characters.");
            if (ProjectId.Length > 0 && !ValidId(ProjectId) || LanguageId.Length > 0 && !ValidId(LanguageId) || LanguageId.Length > 0 && ProjectId.Length == 0) throw new InvalidDataException("The dubbing job has an invalid project or language ID.");
            if (!creating) { if (ProjectId.Length == 0) throw new InvalidDataException("Open an existing dubbing job first."); return; }
            if (ProjectId.Length > 0) throw new InvalidDataException("This job already exists. Resume it instead of submitting it again.");
            if (UseUrl)
            {
                Uri uri;
                if (!Uri.TryCreate(SourceUrl.Trim(), UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Enter a public HTTPS source URL without a username or password.");
            }
            else if (!File.Exists(InputFile) || new FileInfo(InputFile).Length == 0 || new FileInfo(InputFile).Length > 500L * 1024 * 1024) throw new InvalidDataException("Choose an audio or video file between 1 byte and 500 MB.");
        }
        public static DubbingJob Read(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The dubbing job file is too large.");
            var job = JsonData.Serializer().Deserialize<DubbingJob>(File.ReadAllText(path)); if (job == null) throw new InvalidDataException("This is not a supported dubbing job."); job.Validate(false); return job;
        }
        public void Save(string folder)
        {
            Validate(false); FileNames.AtomicText(Path.Combine(folder, ProjectId + ".dubbing.json"), ReadableJson.Format(JsonData.Encode(this)));
        }
    }

    internal static class DubbingLanguages
    {
        // Common choices; an editable code also permits newly supported languages and dialects.
        internal static readonly NamedItem[] Choices = new[] {
            "en|English", "es|Spanish", "fr|French", "de|German", "it|Italian", "pt|Portuguese", "nl|Dutch", "ja|Japanese", "zh|Chinese", "ko|Korean", "ar|Arabic", "hi|Hindi", "pl|Polish", "ru|Russian", "uk|Ukrainian", "sv|Swedish", "da|Danish", "fi|Finnish", "el|Greek", "cs|Czech", "tr|Turkish", "id|Indonesian"
        }.Select(x => new NamedItem { Id = x.Split('|')[0], Name = x.Split('|')[1] + " (" + x.Split('|')[0] + ")" }).ToArray();
        internal static string Code(string value)
        {
            var item = Choices.FirstOrDefault(x => x.Name == value); return item == null ? (value ?? "").Trim() : item.Id;
        }
    }

    internal sealed class DubbingService
    {
        private readonly SpeechClient client;
        internal const string ProjectPath = "/v1/dubbing/project";
        internal DubbingService(SpeechClient client) { this.client = client; }
        internal void Create(DubbingJob job, CancellationToken token)
        {
            job.Validate(true);
            var fields = new Dictionary<string, string> { { "model_id", "dubbing_v2" }, { "target_language", job.TargetLanguage } };
            if (job.Name.Length > 0) fields["reference"] = job.Name;
            if (job.SourceLanguage.Length > 0) fields["source_language"] = job.SourceLanguage;
            var files = new List<UploadFile>();
            if (job.UseUrl) fields["source_url"] = job.SourceUrl.Trim(); else files.Add(new UploadFile("file", job.InputFile));
            var data = JsonData.Object(client.Upload(ProjectPath, fields, files, token));
            job.ProjectId = JsonData.String(data, "project_id");
            if (!DubbingJob.ValidId(job.ProjectId)) throw new InvalidDataException("The server did not return a usable project ID. Check your ElevenLabs dubbing projects before submitting again; the request may have been accepted.");
            object ids;
            if (data.TryGetValue("language_ids", out ids) && ids is System.Collections.IEnumerable)
                job.LanguageId = ((System.Collections.IEnumerable)ids).Cast<object>().Select(x => Convert.ToString(x)).FirstOrDefault() ?? "";
            job.Validate(false);
        }
        internal string Language(DubbingJob job, CancellationToken token)
        {
            job.Validate(false);
            var basePath = ProjectPath + "/" + Uri.EscapeDataString(job.ProjectId);
            var project = JsonData.Object(client.Get(basePath, token));
            CheckFailure(project, "Source preparation");
            if (job.LanguageId.Length == 0)
            {
                var list = JsonData.Object(client.Get(basePath + "/language?page_size=100", token));
                var candidates = JsonData.Items(list, "languages").Where(x => JsonData.String(x, "target_language") == job.TargetLanguage).ToArray();
                if (candidates.Length != 1) throw new InvalidDataException("Could not identify this job's language target. Check the project in ElevenLabs; no new language was submitted.");
                job.LanguageId = JsonData.String(candidates[0], "language_id"); job.Validate(false);
            }
            return client.Get(basePath + "/language/" + Uri.EscapeDataString(job.LanguageId), token);
        }
        internal static Uri CompletedAudio(string json, out string state)
        {
            var data = JsonData.Object(json); state = JsonData.String(data, "status"); CheckFailure(data, "Dubbing");
            if (state == "stale") throw new InvalidDataException("This output is out of date after transcript edits. Regenerate it in ElevenLabs before downloading.");
            if (state == "queued" || state == "processing") return null;
            if (state != "completed") throw new InvalidDataException("The service returned an unrecognised dubbing status: " + state);
            object value; var outputs = data.TryGetValue("outputs", out value) ? value as Dictionary<string, object> : null;
            Uri uri;
            if (!Uri.TryCreate(JsonData.String(outputs, "lossless_audio"), UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("The completed job did not provide a secure audio download.");
            return uri;
        }
        private static void CheckFailure(Dictionary<string, object> data, string title)
        {
            if (JsonData.String(data, "status") != "failed") return;
            object error; var detail = data.TryGetValue("error", out error) ? error as Dictionary<string, object> : null;
            throw new InvalidDataException(title + " failed: " + (JsonData.String(detail, "message").Length > 0 ? JsonData.String(detail, "message") : "Check the project in ElevenLabs for details."));
        }
        internal async Task<string> Wait(DubbingJob job, string folder, Action<string> progress, Action save, CancellationToken token)
        {
            var start = DateTime.UtcNow; string last = "";
            while (DateTime.UtcNow - start < TimeSpan.FromHours(2))
            {
                token.ThrowIfCancellationRequested();
                var json = await Task.Run(() => Language(job, token)); save();
                string state; var uri = CompletedAudio(json, out state);
                if (last != state) { progress("Dubbing status: " + state + "."); last = state; }
                if (uri != null)
                {
                    var path = FileNames.Next(folder, (job.Name.Length > 0 ? job.Name : "Dubbed audio") + " - " + job.TargetLanguage, ".wav");
                    path = await Task.Run(() => Download(uri, path, token)); job.OutputFile = path; save(); return path;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            throw new TimeoutException("Stopped waiting after two hours. Your job is saved; resume it later without submitting it again.");
        }
        internal static string LosslessExtension(byte[] header)
        {
            if (header.Length >= 4 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "fLaC") return ".flac";
            if (header.Length >= 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WAVE") return ".wav";
            throw new InvalidDataException("The server did not return supported lossless audio (WAV or FLAC).");
        }
        internal static string Download(Uri uri, string output, CancellationToken token)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("A secure audio URL is required.");
            var request = (HttpWebRequest)WebRequest.Create(uri); request.Timeout = request.ReadWriteTimeout = 600000;
            var partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                // Signed storage URLs must never receive the ElevenLabs API key.
                using (token.Register(request.Abort)) using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream()) using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The download redirected to an insecure address.");
                    var buffer = new byte[65536]; long total = 0; int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); total += n; if (total > 1024L * 1024 * 1024) throw new InvalidDataException("The dubbed audio exceeds the 1 GB download limit."); file.Write(buffer, 0, n); }
                    if (total < 12) throw new InvalidDataException("The dubbed audio is empty or incomplete."); file.Flush(true);
                }
                using (var file = File.OpenRead(partial)) { var header = new byte[12]; file.Read(header, 0, 12); var ext = LosslessExtension(header); if (Path.GetExtension(output) != ext) output = FileNames.Next(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output), ext); }
                token.ThrowIfCancellationRequested(); File.Move(partial, output); return output;
            }
            catch { token.ThrowIfCancellationRequested(); throw; }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}
