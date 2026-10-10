using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ElevenLabsSpeechGenerator
{
    internal static class ApiTests
    {
        private static int checks;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        public static int Run()
        {
            ServicePointManager.Expect100Continue = false;
            var folder = Path.Combine(AppPaths.AppFolder, "ApiFixtures"); Directory.CreateDirectory(folder);
            var p = new SpeechProject { VoiceId = "voice-a", Text = "Hello" };
            var dub = new DubbingJob { UseUrl = true, SourceUrl = "https://example.invalid/source.mp3", TargetLanguage = "es", Name = "Test dub" };
            Serve(c => new DubbingService(c).Create(dub, CancellationToken.None), "application/json", Encoding.UTF8.GetBytes("{\"project_id\":\"proj_test\",\"language_ids\":[\"lang_test\"],\"status\":\"queued\"}"), (request, body) =>
            {
                Check(request.StartsWith("POST /v1/dubbing/project "), "Dubbing must use the current project API.");
                Check(body.Contains("dubbing_v2") && body.Contains("target_language") && body.Contains("source_url") && !body.Contains("name=\"file\"") && !body.Contains("source_language"), "Dubbing URL or automatic-language payload changed.");
            });
            Check(dub.ProjectId == "proj_test" && dub.LanguageId == "lang_test", "Dubbing IDs must be retained for safe resume.");
            dub.Save(folder); var reopened = DubbingJob.Read(Path.Combine(folder, "proj_test.dubbing.json"));
            Check(reopened.ProjectId == dub.ProjectId && reopened.TargetLanguage == "es", "Dubbing jobs must round trip.");
            bool resubmitted = false;
            try { reopened.Validate(true); resubmitted = true; } catch (InvalidDataException) { }
            Check(!resubmitted, "Existing dubbing jobs must not be resubmitted.");
            string dubState;
            Check(DubbingService.CompletedAudio("{\"status\":\"queued\"}", out dubState) == null && dubState == "queued", "Queued dubbing must remain pending.");
            Check(DubbingService.CompletedAudio("{\"status\":\"completed\",\"outputs\":{\"lossless_audio\":\"https://example.invalid/audio.wav\"}}", out dubState).Scheme == "https", "Completed dubbing output not recognised.");
            foreach (var invalid in new[] { "{\"status\":\"stale\"}", "{\"status\":\"failed\",\"error\":{\"message\":\"fixture failure\"}}", "{\"status\":\"completed\",\"outputs\":{\"lossless_audio\":\"http://example.invalid/audio.wav\"}}", "{\"status\":\"unknown\"}" })
            {
                bool accepted = false; try { DubbingService.CompletedAudio(invalid, out dubState); accepted = true; } catch (InvalidDataException) { }
                Check(!accepted, "Dubbing must reject stale, failed, insecure or unknown responses.");
            }
            Check(!DubbingJob.ValidId("../../escape") && !DubbingJob.ValidId("project\n") && DubbingJob.ValidId("proj_123-abc"), "Dubbing job IDs must not escape paths.");
            Check(DubbingService.LosslessExtension(Encoding.ASCII.GetBytes("fLaCfixture!")) == ".flac", "Dubbing FLAC must not be mislabeled as WAV");
            bool invalidAudio = false; try { DubbingService.LosslessExtension(Encoding.ASCII.GetBytes("not an audio")); } catch (InvalidDataException) { invalidAudio = true; } Check(invalidAudio, "Unknown dubbing audio must be rejected");
            var path = Path.Combine(folder, "speech.mp3");
            Serve((c) => c.Generate(p, null, path, CancellationToken.None), "audio/mpeg", Encoding.ASCII.GetBytes("ID3fixture"), (request, body) =>
            {
                Check(request.StartsWith("POST /v1/text-to-speech/voice-a?output_format=mp3_44100_128 "), "Wrong speech endpoint");
                var json = JsonData.Object(body); Check(JsonData.String(json, "text") == "Hello", "Speech text lost");
                Check(((Dictionary<string, object>)json["voice_settings"]).Count == 2, "V4 settings schema changed");
            });
            Check(File.ReadAllText(path) == "ID3fixture", "Audio response changed");
            p.OutputFormat = "pcm_16000"; path = Path.Combine(folder, "mono.wav");
            Serve(c => c.Generate(p, null, path, CancellationToken.None), "audio/pcm", new byte[32000], null);
            using (var r = new BinaryReader(File.OpenRead(path))) { r.BaseStream.Position = 22; Check(r.ReadInt16() == 1, "Speech PCM must be mono"); Check(r.ReadInt32() == 16000, "PCM rate changed"); Check(r.BaseStream.Length == 32044, "WAV header length"); }
            p.Mode = SpeechMode.Dialogue; p.Dialogue.Add(new DialogueLine { VoiceId = "voice-b", VoiceName = "B", Text = "Hello again" }); path = Path.Combine(folder, "dialogue.wav");
            Serve(c => c.Generate(p, null, path, CancellationToken.None), "audio/pcm", new byte[40], (request, body) =>
            {
                Check(request.Contains("/v1/text-to-dialogue?"), "Wrong dialogue endpoint"); var d = JsonData.Object(body); Check(d.ContainsKey("inputs") && d.ContainsKey("settings") && !d.ContainsKey("voice_settings"), "Wrong dialogue payload");
            });
            p.InputFile = Path.Combine(folder, "input.wav"); File.WriteAllBytes(p.InputFile, new byte[80]); p.Mode = SpeechMode.Transcription; p.ModelId = "scribe_v2";
            var cloneProgress = new List<string>();
            Serve(c => c.Upload("/v1/voices/add", new Dictionary<string, string> { { "name", "Fixture clone" } }, new List<UploadFile> { new UploadFile("files", p.InputFile) }, CancellationToken.None, cloneProgress.Add), "application/json", Encoding.UTF8.GetBytes("{\"voice_id\":\"fixture\"}"), (request, body) =>
                Check(request.Contains("/v1/voices/add ") && body.Contains("Fixture clone") && body.Contains("name=\"files\""), "Clone upload lost its fields or samples"));
            Check(cloneProgress.Exists(x => x.Contains("100%")) && cloneProgress[cloneProgress.Count - 1].Contains("Waiting for ElevenLabs"), "Clone progress must distinguish upload completion from server processing");
            Serve(c => { var result = c.Generate(p, null, path, CancellationToken.None); Check(JsonData.String(JsonData.Object(result.Json), "text") == "Transcript", "Transcript was not returned as JSON"); }, "application/json", Encoding.UTF8.GetBytes("{\"text\":\"Transcript\"}"), (request, body) =>
            {
                Check(request.Contains("/v1/speech-to-text "), "Wrong transcription endpoint"); Check(body.Contains("name=\"file\"") && body.Contains("name=\"diarize\"") && body.Contains("timestamps_granularity") && body.Contains("scribe_v2"), "Missing multipart transcription fields");
            });
            p.Mode = SpeechMode.VoiceChanger; p.ModelId = "eleven_multilingual_sts_v2"; path = Path.Combine(folder, "changed.wav");
            Serve(c => c.Generate(p, null, path, CancellationToken.None), "audio/pcm", new byte[40], (request, body) =>
            {
                Check(request.Contains("/v1/speech-to-speech/voice-a?"), "Wrong conversion endpoint"); Check(body.Contains("name=\"audio\"") && body.Contains("name=\"voice_settings\"") && body.Contains("remove_background_noise"), "Missing conversion fields");
            });
            p.Mode = SpeechMode.VoiceDesign; p.Text = "A warm British adult narrator."; p.ModelId = "eleven_ttv_v3";
            Serve(c => { var result = c.Generate(p, null, path, CancellationToken.None); Check(result.File == null && result.Json.Contains("previews"), "Voice design must return previews"); }, "application/json", Encoding.UTF8.GetBytes("{\"previews\":[]}"), (request, body) => Check(request.Contains("/v1/text-to-voice/design?") && JsonData.Bool(JsonData.Object(body), "auto_generate_text"), "Voice design request changed"));
            p.Mode = SpeechMode.VoiceRemix;
            Serve(c => c.Generate(p, null, path, CancellationToken.None), "application/json", Encoding.UTF8.GetBytes("{\"previews\":[]}"), (request, body) =>
            {
                var d = JsonData.Object(body); Check(request.Contains("/v1/text-to-voice/voice-a/remix?"), "Wrong voice remix endpoint");
                Check(JsonData.String(d, "voice_description") == p.Text && !d.ContainsKey("model_id"), "Voice remix must use the documented instruction schema");
            });
            p.Mode = SpeechMode.ForcedAlignment; p.Text = "Hello world";
            Serve(c => c.Generate(p, null, null, CancellationToken.None), "application/json", Encoding.UTF8.GetBytes("{\"words\":[{\"text\":\"Hello\",\"start\":0,\"end\":1},{\"text\":\"world\",\"start\":1,\"end\":2}]}"), (request, body) =>
                Check(request.Contains("/v1/forced-alignment ") && body.Contains("name=\"file\"") && body.Contains("Hello world") && !body.Contains("model_id"), "Wrong alignment multipart fields"));
            p.Mode = SpeechMode.VoiceIsolation; path = Path.Combine(folder, "isolated.wav");
            var wave = new MemoryStream(); SpeechClient.WriteWaveHeader(wave, 16000, 1, 0);
            Serve(c => c.Generate(p, null, path, CancellationToken.None), "audio/wav", wave.ToArray(), (request, body) =>
                Check(request.Contains("/v1/audio-isolation ") && body.Contains("name=\"audio\"") && body.Contains("file_format") && !body.Contains("voice_settings"), "Wrong isolation multipart fields"));
            Check(File.ReadAllBytes(path).Length == 44, "Isolation must preserve the supplied WAV header");
            path = Path.Combine(folder, "isolated-mp3.wav");
            Serve(c => { var r = c.Generate(p, null, path, CancellationToken.None); Check(Path.GetExtension(r.File) == ".mp3" && !File.Exists(path), "Isolation must name the actual returned MP3 format"); }, "audio/mpeg", Encoding.ASCII.GetBytes("ID3fixtureaudio"), null);
            path = Path.Combine(folder, "dictionary.pls");
            Serve(c => c.Download("/v1/pronunciation-dictionaries/id/version/download", path, CancellationToken.None), "application/pls+xml", Encoding.UTF8.GetBytes("<lexicon/>"), null);
            Check(File.ReadAllText(path) == "<lexicon/>", "PLS export must preserve XML");
            Serve(c => c.Delete("/v1/voices/disposable", CancellationToken.None), "application/json", new byte[0], (request, body) => Check(request.StartsWith("DELETE "), "Delete method lost"), 204);
            bool rejected = false;
            Serve(c => { try { c.Get("/v1/models", CancellationToken.None); } catch (ElevenLabsApiException e) { rejected = e.StatusCode == HttpStatusCode.InternalServerError; } }, "application/json", Encoding.UTF8.GetBytes("{\"detail\":{\"message\":\"Fixture failure\"}}"), null, 500);
            Check(rejected, "Service error status lost");
            rejected = false;
            Serve(c => { try { c.Get("/v1/models", CancellationToken.None); } catch (ElevenLabsApiException e) { rejected = (int)e.StatusCode == 302; } }, "text/plain", new byte[0], null, 302);
            Check(rejected, "Authenticated redirect must fail closed");
            path = Path.Combine(folder, "existing.mp3"); File.WriteAllText(path, "Keep existing"); rejected = false;
            Serve(c => { try { c.Download("/audio", path, CancellationToken.None); } catch (IOException) { rejected = true; } }, "audio/mpeg", new byte[30], null);
            Check(rejected && File.ReadAllText(path) == "Keep existing", "Existing output overwritten");
            Check(Directory.GetFiles(folder, "*.partial").Length == 0, "Failed download left partial files");
            using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); try { new SpeechClient("fixture").Get("/v1/models", cancelled.Token); throw new Exception("Cancelled request sent"); } catch (OperationCanceledException) { checks++; } }
            return checks;
        }
        private static void Serve(Action<SpeechClient> action, string type, byte[] payload, Action<string, string> inspect, int status = 200)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() =>
            {
                using (var socket = listener.AcceptTcpClient()) using (var stream = socket.GetStream())
                {
                    stream.ReadTimeout = stream.WriteTimeout = 5000;
                    var headers = new MemoryStream(); int b;
                    while ((b = stream.ReadByte()) >= 0) { headers.WriteByte((byte)b); if (headers.Length > 65536) throw new Exception("Unbounded fixture header"); var bytes = headers.GetBuffer(); int n = (int)headers.Length; if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) break; }
                    string header = Encoding.ASCII.GetString(headers.ToArray()); int length = 0;
                    foreach (var line in header.Split(new[] { "\r\n" }, StringSplitOptions.None)) if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                    if (length > 1024 * 1024) throw new Exception("Fixture body too large");
                    var body = new byte[length]; int read = 0; while (read < length) { int n = stream.Read(body, read, length - read); if (n == 0) throw new EndOfStreamException(); read += n; }
                    if (inspect != null) inspect(header.Split('\r')[0], Encoding.UTF8.GetString(body));
                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Fixture\r\nContent-Type: " + type + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n" + (status == 302 ? "Location: https://example.invalid/\r\n" : "") + "\r\n");
                    stream.Write(response, 0, response.Length); stream.Write(payload, 0, payload.Length);
                }
            });
            try { action(new SpeechClient("fixture-not-a-key", "http://127.0.0.1:" + port)); if (!server.Wait(6000)) throw new Exception("Fixture server did not finish"); }
            finally { listener.Stop(); if (!server.IsCompleted) try { server.Wait(6000); } catch (AggregateException) { } }
        }
    }
}
