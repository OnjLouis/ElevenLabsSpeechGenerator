using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NAudio.Wave;

namespace ElevenLabsSpeechGenerator
{
    internal static class LongSpeech
    {
        public const int MaximumCharacters = 500000;
        private const long MaximumWaveBytes = 1024L * 1024 * 1024;
        private const int ContextCharacters = 1000;

        public static List<string> Split(string text, int limit)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumCharacters || limit < 2)
                throw new InvalidDataException("Enter speech text of up to 500,000 characters.");
            var boundaries = new HashSet<int>(StringInfo.ParseCombiningCharacters(text)); boundaries.Add(text.Length);
            int lastNonblank = text.Length - 1; while (lastNonblank >= 0 && char.IsWhiteSpace(text[lastNonblank])) lastNonblank--;
            var result = new List<string>(); int start = 0;
            while (text.Length - start > limit)
            {
                int end = start + limit, word = -1, sentence = -1, paragraph = -1, brackets = 0; char lastContent = '\0';
                for (int i = start; i < end; i++)
                {
                    char c = text[i];
                    if (!char.IsWhiteSpace(c) && "\"'\u201d\u2019)".IndexOf(c) < 0) lastContent = c;
                    if (c == '[') brackets++;
                    else if (c == ']' && brackets > 0) brackets--;
                    if (brackets != 0) continue;
                    int boundary = i + 1;
                    if (!boundaries.Contains(boundary) || boundary > lastNonblank) continue;
                    if (boundary < text.Length && text[i] == '\r' && text[boundary] == '\n') continue;
                    if (char.IsWhiteSpace(c))
                    {
                        word = boundary;
                        if (boundary - start >= limit / 2)
                        {
                            if (".!?\u3002\uff01\uff1f".IndexOf(lastContent) >= 0) sentence = boundary;
                            if (c == '\n' || c == '\r') paragraph = boundary;
                        }
                    }
                    else if ("\u3002\uff01\uff1f".IndexOf(c) >= 0) { word = boundary; if (boundary - start >= limit / 2) sentence = boundary; }
                }
                int cut = paragraph > start ? paragraph : sentence > start ? sentence : word;
                if (cut <= start || string.IsNullOrWhiteSpace(text.Substring(start, cut - start)))
                    throw new InvalidDataException("A word, speech tag, or blank passage exceeds this model's request limit. Shorten that passage before generating.");
                result.Add(text.Substring(start, cut - start)); start = cut;
            }
            var tail = text.Substring(start);
            if (string.IsNullOrWhiteSpace(tail))
                throw new InvalidDataException("The text ends with an overlong blank passage. Remove the extra whitespace before generating.");
            result.Add(tail); return result;
        }

        public static int ModelLimit(NamedItem model, string id)
        {
            int limit;
            if (model != null && int.TryParse(JsonData.String(model.Data, "maximum_text_length_per_request"), out limit) && limit > 0) return limit;
            return id == "eleven_v3" ? 5000 : id == "eleven_flash_v2_5" || id == "eleven_turbo_v2_5" ? 40000 : id == "eleven_flash_v2" ? 30000 : 10000;
        }

        public static int SampleRate(string format) { return int.Parse(format.Split('_')[1], CultureInfo.InvariantCulture); }

        public sealed class Part
        {
            public string Hash { get; set; }
            public string RequestId { get; set; }
            public DateTime CreatedUtc { get; set; }
        }
        public sealed class Recording
        {
            public List<Part> Parts { get; set; }
            public string OutputName { get; set; }
            public string OutputHash { get; set; }
        }
        public sealed class Checkpoint
        {
            public int Version { get; set; }
            public string Fingerprint { get; set; }
            public List<Recording> Recordings { get; set; }
        }

        private static string Fingerprint(SpeechProject request, NamedItem model, int limit)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonData.Encode(request) + "\n" + limit + "\n" + JsonData.Encode(request.VoiceSettings(model)));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static List<ApiResult> Generate(SpeechProject request, NamedItem model, string folder, string stem, int limit,
            CancellationToken token, Action<string> progress, Func<SpeechProject, Dictionary<string, object>, string, CancellationToken, ApiResult> generate)
        {
            if (request.Mode != SpeechMode.TextToSpeech) throw new InvalidDataException("Long-text splitting is available only for Text to speech.");
            request.Validate(MaximumCharacters); var chunks = Split(request.Text, limit);
            string fingerprint = Fingerprint(request, model, limit);
            string job = Path.Combine(folder, "Details", "Unfinished speech", fingerprint), manifest = Path.Combine(job, "Progress.json");
            string jobs = Path.GetDirectoryName(job); Directory.CreateDirectory(jobs);
            var results = new List<ApiResult>();
            using (var lease = new FileStream(Path.Combine(jobs, "Request.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
            {
                Directory.CreateDirectory(job);
                if ((File.GetAttributes(job) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The unfinished speech folder must not redirect to another location.");
                Checkpoint checkpoint;
                if (File.Exists(manifest))
                {
                    if (new FileInfo(manifest).Length > 2 * 1024 * 1024) throw new InvalidDataException("The unfinished speech record is too large.");
                    checkpoint = JsonData.Serializer().Deserialize<Checkpoint>(File.ReadAllText(manifest));
                    if (checkpoint == null || checkpoint.Version != 1 || checkpoint.Fingerprint != fingerprint || checkpoint.Recordings == null || checkpoint.Recordings.Count != request.Variations || checkpoint.Recordings.Any(x => x == null || x.Parts == null || x.Parts.Count > chunks.Count))
                        throw new InvalidDataException("The unfinished speech record does not match this request. No audio was sent.");
                }
                else
                {
                    if (Directory.EnumerateFileSystemEntries(job).Any()) throw new InvalidDataException("Unrecorded speech parts need inspection before this request can resume.");
                    checkpoint = new Checkpoint { Version = 1, Fingerprint = fingerprint, Recordings = Enumerable.Range(0, request.Variations).Select(x => new Recording { Parts = new List<Part>() }).ToList() };
                    Save(manifest, checkpoint);
                }
                // Validate every retained file before making another paid request.
                for (int v = 0; v < checkpoint.Recordings.Count; v++)
                {
                    var recording = checkpoint.Recordings[v];
                    for (int i = 0; i < recording.Parts.Count; i++)
                    {
                        var path = PartPath(job, v, i, request.OutputFormat);
                        ValidateFile(path, recording.Parts[i] == null ? null : recording.Parts[i].Hash);
                        using (var audio = OpenAudio(path)) ValidateAudio(audio);
                    }
                    if (recording.OutputHash != null)
                    {
                        if (string.IsNullOrEmpty(recording.OutputName) || Path.GetFileName(recording.OutputName) != recording.OutputName || Path.GetExtension(recording.OutputName) != ".wav") throw new InvalidDataException("Invalid completed speech filename.");
                        ValidateFile(Path.Combine(folder, recording.OutputName), recording.OutputHash);
                    }
                }
                for (int v = 0; v < request.Variations; v++)
                {
                    token.ThrowIfCancellationRequested(); var recording = checkpoint.Recordings[v];
                    if (recording.OutputHash != null) { progress("Keeping completed variation " + (v + 1) + "."); results.Add(new ApiResult { File = Path.Combine(folder, recording.OutputName) }); continue; }
                    for (int i = recording.Parts.Count; i < chunks.Count; i++)
                    {
                        token.ThrowIfCancellationRequested(); progress("Generating variation " + (v + 1) + ", part " + (i + 1) + " of " + chunks.Count + ".");
                        var partRequest = JsonData.Serializer().Deserialize<SpeechProject>(JsonData.Encode(request)); partRequest.Text = chunks[i]; partRequest.Variations = 1;
                        var context = Context(request.ModelId, chunks, i, recording.Parts);
                        var path = PartPath(job, v, i, request.OutputFormat);
                        if (File.Exists(path)) throw new InvalidDataException("A completed but unrecorded speech part needs inspection. It was not overwritten or generated again.");
                        var result = generate(partRequest, context, path, token);
                        if (result.File != path) throw new InvalidDataException("The speech service did not save the expected part.");
                        recording.Parts.Add(new Part { Hash = Hash(path), RequestId = result.RequestId, CreatedUtc = DateTime.UtcNow }); Save(manifest, checkpoint);
                        using (var audio = OpenAudio(path)) ValidateAudio(audio);
                    }
                    var output = FileNames.Next(folder, stem + (request.Variations > 1 ? "_v" + (v + 1) : ""), ".wav");
                    progress("Assembling variation " + (v + 1) + " into one WAV.");
                    Assemble(Enumerable.Range(0, chunks.Count).Select(i => PartPath(job, v, i, request.OutputFormat)), output, token);
                    recording.OutputName = Path.GetFileName(output); recording.OutputHash = Hash(output); Save(manifest, checkpoint);
                    results.Add(new ApiResult { File = output, RequestId = recording.Parts.Last().RequestId });
                }
                // Delete only this job's known files, not a parent output folder.
                for (int v = 0; v < request.Variations; v++) for (int i = 0; i < chunks.Count; i++) File.Delete(PartPath(job, v, i, request.OutputFormat));
                File.Delete(manifest);
                if (!Directory.EnumerateFileSystemEntries(job).Any()) Directory.Delete(job);
            }
            return results;
        }

        internal static Dictionary<string, object> Context(string model, List<string> chunks, int index, List<Part> previous)
        {
            var result = new Dictionary<string, object>(); if (model == "eleven_v3") return result;
            if (index + 1 < chunks.Count) result["next_text"] = Prefix(chunks[index + 1]);
            if (index > 0)
            {
                var ids = previous.Skip(Math.Max(0, previous.Count - 3)).Where(x => !string.IsNullOrEmpty(x.RequestId) && DateTime.UtcNow - x.CreatedUtc < TimeSpan.FromHours(2)).Select(x => x.RequestId).ToArray();
                if (ids.Length > 0) result["previous_request_ids"] = ids;
                else result["previous_text"] = Suffix(chunks[index - 1]);
            }
            return result;
        }
        private static string Prefix(string text) { int n = Math.Min(ContextCharacters, text.Length); if (n < text.Length && char.IsHighSurrogate(text[n - 1])) n--; return text.Substring(0, n); }
        private static string Suffix(string text) { int n = Math.Max(0, text.Length - ContextCharacters); if (n > 0 && char.IsLowSurrogate(text[n])) n++; return text.Substring(n); }
        private static string PartPath(string job, int v, int i, string format) { return Path.Combine(job, "v" + (v + 1) + "-part" + (i + 1) + (format.StartsWith("pcm_", StringComparison.Ordinal) ? ".wav" : ".mp3")); }
        private static void Save(string path, Checkpoint data) { FileNames.AtomicText(path, ReadableJson.Format(JsonData.Encode(data))); }
        private static string Hash(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > MaximumWaveBytes) throw new InvalidDataException("A speech recording is redirected or too large.");
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static void ValidateFile(string path, string hash)
        {
            if (string.IsNullOrEmpty(hash) || !File.Exists(path) || Hash(path) != hash) throw new InvalidDataException("A retained speech recording is missing or changed. No request was sent.");
        }
        private static WaveStream OpenAudio(string path) { return Path.GetExtension(path) == ".mp3" ? (WaveStream)new Mp3FileReader(path) : new WaveFileReader(path); }
        private static void ValidateAudio(WaveStream reader)
        {
            var format = reader.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels < 1 || format.Channels > 2 || reader.Length == 0 || reader.Length % format.BlockAlign != 0)
                throw new InvalidDataException("A speech part could not be decoded as complete PCM audio.");
        }
        internal static void Assemble(IEnumerable<string> parts, string output, CancellationToken token)
        {
            string partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                WaveFileWriter writer = null;
                try
                {
                    long bytes = 0;
                    foreach (var path in parts)
                    {
                        token.ThrowIfCancellationRequested();
                        using (WaveStream reader = OpenAudio(path))
                        {
                            ValidateAudio(reader);
                            var format = reader.WaveFormat;
                            if (writer == null) writer = new WaveFileWriter(partial, format);
                            else if (!writer.WaveFormat.Equals(format)) throw new InvalidDataException("Speech parts have different audio formats.");
                            bytes = checked(bytes + reader.Length); if (bytes > MaximumWaveBytes) throw new InvalidDataException("The assembled recording exceeds 1 GB. Generate shorter passages.");
                            var buffer = new byte[65536]; int n;
                            while ((n = reader.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); writer.Write(buffer, 0, n); }
                        }
                    }
                    if (writer == null) throw new InvalidDataException("No speech parts were available.");
                }
                finally { if (writer != null) writer.Dispose(); }
                token.ThrowIfCancellationRequested(); File.Move(partial, output);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}
