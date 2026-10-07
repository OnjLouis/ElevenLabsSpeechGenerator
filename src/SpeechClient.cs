using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class ElevenLabsApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; private set; }
        public string ResponseBody { get; private set; }
        public ElevenLabsApiException(string message, HttpStatusCode? status, string body, Exception inner) : base(message, inner) { StatusCode = status; ResponseBody = body; }
    }
    internal sealed class ApiResult { public string Json; public string File; public string RequestId; }
    internal sealed class UploadFile
    {
        public string Field; public string Path;
        public UploadFile(string field, string path) { Field = field; Path = path; }
    }
    internal sealed class SpeechClient
    {
        private readonly string key;
        private readonly string origin;
        private const int Timeout = 600000;
        public SpeechClient(string key) : this(key, "https://api.elevenlabs.io") { }
        internal SpeechClient(string key, string origin) { this.key = (key ?? "").Trim(); this.origin = origin.TrimEnd('/'); }
        public string Get(string path, CancellationToken token) { return Send("GET", path, null, null, null, null, 0, token).Json; }
        public string Post(string path, object body, CancellationToken token) { return Send("POST", path, body, null, null, null, 0, token).Json; }
        public string Upload(string path, Dictionary<string, string> fields, List<UploadFile> files, CancellationToken token) { return Send("POST", path, null, fields, files, null, 0, token).Json; }
        public ApiResult Download(string path, string output, CancellationToken token) { return Send("GET", path, null, null, null, output, 0, token); }
        public void Delete(string path, CancellationToken token) { Send("DELETE", path, null, null, null, null, 0, token); }
        internal static string PreviewExtension(Dictionary<string, object> preview)
        {
            var type = JsonData.String(preview, "media_type").Split(';')[0].Trim().ToLowerInvariant();
            if (type == "audio/mpeg" || type == "audio/mp3") return ".mp3";
            if (type == "audio/wav" || type == "audio/x-wav" || type == "audio/wave") return ".wav";
            throw new InvalidDataException("The voice preview uses an unsupported audio format.");
        }
        internal static string IsolationExtension(byte[] header)
        {
            if (header.Length >= 3 && (Encoding.ASCII.GetString(header, 0, 3) == "ID3" || header[0] == 255 && (header[1] & 0xe0) == 0xe0 && (header[1] & 6) == 2 && (header[1] & 0x18) != 8 && (header[2] & 0xf0) != 0xf0)) return ".mp3";
            return DubbingService.LosslessExtension(header);
        }
        public ApiResult Generate(SpeechProject p, NamedItem model, string output, CancellationToken token)
        {
            if (p.Mode == SpeechMode.VoiceIsolation || p.Mode == SpeechMode.ForcedAlignment)
                return Send("POST", p.Mode == SpeechMode.VoiceIsolation ? "/v1/audio-isolation" : "/v1/forced-alignment", null,
                    p.Mode == SpeechMode.VoiceIsolation ? new Dictionary<string, string> { { "file_format", "other" } } : new Dictionary<string, string> { { "text", p.Text } },
                    new List<UploadFile> { new UploadFile(p.Mode == SpeechMode.VoiceIsolation ? "audio" : "file", p.InputFile) }, p.Mode == SpeechMode.VoiceIsolation ? output : null, 0, token);
            string suffix = "?output_format=" + Uri.EscapeDataString(p.OutputFormat);
            int channels = 1; string path;
            if (p.Mode == SpeechMode.Transcription || p.Mode == SpeechMode.VoiceChanger)
            {
                var fields = new Dictionary<string, string> { { "model_id", p.ModelId } }; var files = new List<UploadFile>();
                if (p.Mode == SpeechMode.Transcription)
                {
                    path = "/v1/speech-to-text"; files.Add(new UploadFile("file", p.InputFile));
                    fields["diarize"] = p.Diarize ? "true" : "false"; fields["tag_audio_events"] = p.AudioEvents ? "true" : "false"; fields["timestamps_granularity"] = "word";
                    if (!string.IsNullOrWhiteSpace(p.Language)) fields["language_code"] = p.Language;
                }
                else
                {
                    path = "/v1/speech-to-speech/" + Uri.EscapeDataString(p.VoiceId) + suffix; files.Add(new UploadFile("audio", p.InputFile));
                    fields["voice_settings"] = JsonData.Encode(p.VoiceSettings(model)); fields["remove_background_noise"] = p.RemoveNoise ? "true" : "false";
                }
                return Send("POST", path, null, fields, files, p.Mode == SpeechMode.Transcription ? null : output, channels, token);
            }
            path = p.Mode == SpeechMode.Dialogue ? "/v1/text-to-dialogue" : p.Mode == SpeechMode.VoiceDesign ? "/v1/text-to-voice/design" : p.Mode == SpeechMode.VoiceRemix ? "/v1/text-to-voice/" + Uri.EscapeDataString(p.VoiceId) + "/remix" : "/v1/text-to-speech/" + Uri.EscapeDataString(p.VoiceId);
            return Send("POST", path + suffix, p.JsonBody(model), null, null, p.Mode == SpeechMode.VoiceDesign || p.Mode == SpeechMode.VoiceRemix ? null : output, channels, token);
        }
        private ApiResult Send(string method, string path, object body, Dictionary<string, string> fields, List<UploadFile> files, string output, int channels, CancellationToken token)
        {
            if (key.Length == 0) throw new InvalidOperationException("Enter an API key in Preferences first.");
            token.ThrowIfCancellationRequested(); var request = ApiTransport.Create(origin + path, method, key, Timeout); string temp = null;
            using (token.Register(request.Abort))
            {
                try
                {
                    if (fields != null) WriteMultipart(request, fields, files, token);
                    else if (body != null)
                    {
                        var data = Encoding.UTF8.GetBytes(JsonData.Encode(body)); request.ContentType = "application/json"; request.ContentLength = data.Length;
                        using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                    }
                    using (var response = request.GetResponse()) using (var stream = response.GetResponseStream())
                    {
                        var result = new ApiResult { RequestId = response.Headers["request-id"] };
                        if (output == null)
                        {
                            using (var buffer = new MemoryStream()) { Copy(stream, buffer, 16L * 1024 * 1024, token); result.Json = Encoding.UTF8.GetString(buffer.ToArray()); }
                            return result;
                        }
                        if ((response.ContentType ?? "").IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidDataException("The server returned JSON instead of audio.");
                        temp = output + "." + Guid.NewGuid().ToString("N") + ".partial";
                        using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        {
                            bool pcm = path.Contains("output_format=pcm_"); if (pcm) file.Position = 44;
                            Copy(stream, file, 1024L * 1024 * 1024, token);
                            if (file.Length <= (pcm ? 44 : 0)) throw new InvalidDataException("The server returned empty audio.");
                            if (pcm)
                            {
                                const string prefix = "output_format=pcm_";
                                int rate = int.Parse(path.Substring(path.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Split('&')[0], CultureInfo.InvariantCulture);
                                WriteWaveHeader(file, rate, channels, checked((int)file.Length - 44));
                            }
                            if (path == "/v1/audio-isolation")
                            {
                                file.Position = 0; var header = new byte[12]; int count = file.Read(header, 0, header.Length);
                                if (count < 12) throw new InvalidDataException("The isolation service returned incomplete audio.");
                                var ext = IsolationExtension(header); if (Path.GetExtension(output) != ext) output = FileNames.Next(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output), ext);
                            }
                            file.Flush(true);
                        }
                        token.ThrowIfCancellationRequested(); File.Move(temp, output); temp = null; result.File = output; return result;
                    }
                }
                catch (WebException ex)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    var response = ex.Response as HttpWebResponse; if (response == null) throw new IOException("Could not contact ElevenLabs: " + ex.Message, ex);
                    using (response) using (var stream = response.GetResponseStream()) using (var buffer = new MemoryStream())
                    {
                        Copy(stream, buffer, 1024 * 1024, CancellationToken.None); string text = Encoding.UTF8.GetString(buffer.ToArray());
                        throw new ElevenLabsApiException("ElevenLabs returned HTTP " + (int)response.StatusCode + ": " + ExtractErrorMessage(text), response.StatusCode, text, ex);
                    }
                }
                finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
            }
        }
        private static void WriteMultipart(IApiRequest request, Dictionary<string, string> fields, List<UploadFile> files, CancellationToken token)
        {
            var boundary = "Speech" + Guid.NewGuid().ToString("N"); request.ContentType = "multipart/form-data; boundary=" + boundary;
            var parts = new List<byte[]>(); long length = 0;
            foreach (var field in fields) { var bytes = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + field.Key + "\"\r\n\r\n" + field.Value + "\r\n"); parts.Add(bytes); length += bytes.Length; }
            var headers = new List<byte[]>();
            foreach (var f in files)
            {
                var info = new FileInfo(f.Path); if (info.Length > 500L * 1024 * 1024) throw new InvalidDataException("Select an audio file smaller than 500 MB.");
                var name = info.Name.Replace("\"", "_").Replace("\r", "_").Replace("\n", "_");
                var h = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + f.Field + "\"; filename=\"" + name + "\"\r\nContent-Type: application/octet-stream\r\n\r\n"); headers.Add(h); length += h.Length + info.Length + 2;
            }
            var end = Encoding.ASCII.GetBytes("--" + boundary + "--\r\n"); request.ContentLength = length + end.Length;
            if (CurlTransport.UseForCurrentSystem && request.ContentLength > 25L * 1024 * 1024) throw new InvalidDataException("On Windows 7, select an upload smaller than 25 MB.");
            using (var stream = request.GetRequestStream())
            {
                foreach (var bytes in parts) stream.Write(bytes, 0, bytes.Length);
                for (int i = 0; i < files.Count; i++) { stream.Write(headers[i], 0, headers[i].Length); using (var file = File.OpenRead(files[i].Path)) Copy(file, stream, 500L * 1024 * 1024, token); stream.WriteByte(13); stream.WriteByte(10); }
                stream.Write(end, 0, end.Length);
            }
        }
        private static void Copy(Stream input, Stream output, long maximum, CancellationToken token)
        {
            var buffer = new byte[65536]; long total = 0; int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); total += n; if (total > maximum) throw new InvalidDataException("The response or upload exceeds the supported size."); output.Write(buffer, 0, n); }
        }
        internal static void WriteWaveHeader(Stream file, int rate, int channels, int bytes)
        {
            if (bytes % (channels * 2) != 0) throw new InvalidDataException("The PCM audio is incomplete.");
            file.Position = 0; using (var w = new BinaryWriter(file, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(bytes + 36); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            }
        }
        public static string ExtractErrorMessage(string text)
        {
            try { var d = JsonData.Object(text); object v; if (d.TryGetValue("detail", out v)) { var detail = v as Dictionary<string, object>; return detail != null ? JsonData.String(detail, "message") : Convert.ToString(v); } return JsonData.String(d, "message"); }
            catch { return "The service rejected this request."; }
        }
    }
}
