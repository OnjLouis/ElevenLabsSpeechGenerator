using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;

namespace ElevenLabsSpeechGenerator
{
    internal static class LongSpeechTests
    {
        private static int checks;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        private static void Reject(Action action, string message) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception(message); }
        public static int Run()
        {
            var cases = new[] { "First paragraph.\r\nAnother sentence, with enough words for several parts. Last sentence.  ", "A long sentence without a full stop but with words at every possible boundary", "One [speaking very softly] word. Another sentence.", "Emoji \ud83d\ude00 and accents e\u0301. More words. End.", "\u4e00\u4e8c\u4e09\u3002\u56db\u4e94\u516d\u3002\u4e03\u516b\u4e5d\u3002" };
            foreach (var text in cases)
            {
                int limit = text.Contains("[") ? 32 : 25;
                var parts = LongSpeech.Split(text, limit);
                Check(string.Concat(parts) == text && parts.All(x => x.Length <= limit && !string.IsNullOrWhiteSpace(x)), "Split lost text or exceeded its limit");
                var valid = new HashSet<int>(StringInfo.ParseCombiningCharacters(text)); valid.Add(text.Length); int index = 0;
                foreach (var part in parts) { index += part.Length; Check(valid.Contains(index), "Split broke a Unicode character"); Check(!part.EndsWith("\r"), "Split broke CRLF"); }
            }
            var sentence = new string('a', 9978) + ". " + "Next sentence has more than twenty characters.";
            Check(LongSpeech.Split(sentence, 10000)[0].Length == 9980, "Did not split at the sentence before 10,000");
            Reject(() => LongSpeech.Split(new string('a', 10001), 10000), "Overlong unbroken word was chopped");
            Reject(() => LongSpeech.Split("One [" + new string('a', 30) + "] word", 20), "Speech tag was chopped");
            Reject(() => LongSpeech.Split(new string('x', LongSpeech.MaximumCharacters + 1), 10000), "Long input was unbounded");
            Check(LongSpeech.ModelLimit(null, "eleven_v3") == 5000 && LongSpeech.ModelLimit(null, "eleven_v4") == 10000, "Model limit fallback changed");
            var previous = Enumerable.Range(0, 4).Select(i => new LongSpeech.Part { RequestId = "id" + i, CreatedUtc = DateTime.UtcNow }).ToList();
            var chunks = new List<string> { "One.", "Two.", "Three." };
            var context = LongSpeech.Context("eleven_v4", chunks, 1, previous);
            Check(((string[])context["previous_request_ids"]).SequenceEqual(new[] { "id1", "id2", "id3" }) && (string)context["next_text"] == "Three.", "Stitching context was not bounded to three request IDs");
            previous.ForEach(x => x.CreatedUtc = DateTime.UtcNow.AddHours(-3));
            Check(LongSpeech.Context("eleven_v4", chunks, 1, previous).ContainsKey("previous_text"), "Expired IDs were sent for stitching");
            Check(LongSpeech.Context("eleven_v3", chunks, 1, previous).Count == 0, "Unsupported v3 stitching fields were sent");
            TestResume();
            TestMp3Assembly();
            return checks;
        }
        private static ApiResult WritePart(string path, int value)
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1))) writer.Write(new byte[] { (byte)value, 0, (byte)value, 0 }, 0, 4);
            return new ApiResult { File = path, RequestId = "part" + value };
        }
        private static void TestMp3Assembly()
        {
            string folder = Path.Combine(AppPaths.AppFolder, "MP3 assembly fixture"); Directory.CreateDirectory(folder);
            try
            {
                var request = new SpeechProject { VoiceId = "test", Text = "First sentence. Second sentence.", OutputFormat = "mp3_44100_128", Variations = 1 };
                int calls = 0;
                var outputs = LongSpeech.Generate(request, null, folder, "MP3 fixture", 20, CancellationToken.None, message => { }, (part, context, path, token) =>
                {
                    Check(part.OutputFormat == request.OutputFormat && Path.GetExtension(path) == ".mp3", "Long speech silently requested a higher-tier PCM format");
                    File.WriteAllBytes(path, Convert.FromBase64String(Tone)); calls++; return new ApiResult { File = path, RequestId = "mp3" + calls };
                });
                using (var wave = new WaveFileReader(outputs.Single().File))
                    Check(wave.WaveFormat.SampleRate == 44100 && wave.Length > 0 && wave.TotalTime.TotalSeconds > 0.1 * calls, "MP3 parts were not decoded into a complete WAV");
            }
            finally { Directory.Delete(folder, true); }
        }
        // A generated 100 ms sine tone; no third-party recording is included.
        private const string Tone = "//sQxAAABHQTVVSQgDCmCa83GiACAAGtOUAAAVk6PVBQCAYJAfB8HwfKAgCAYRB8H9QIOxOH+INwBJP2wGA4HA4AAAAAACiJKpkUZAjpAkgWo/eFAfATG/AilC+oGhL8JA0qCgAYMAD/+xLEAoPFWB0gHeAAKJsD40GvaEzMCQC8QASGAOB4Z+72pmMDlmHEESYMAH5gQgYGBSBMYF4DxZq0lYeYIIZk+cS0YX4opqvUomp+KKYYQMxz3pn0pmjxm45iQriU4Ju+mjDQ4w4dMdP/+xDEA4PFBB8YDfsiQK2EYoG/bEiDPoMwrxvjUM4cNOsbQwngbTXMAgZuqHV+awbJpaHP1/QkQYmImbHBu8aYlw7RwV9cG/wO4YmoT5wjIZ4jGZn5mTwYsLMHjFPgH/q+ijChEw8cMf/7EsQDA8UAHxgN+yJAtAQkArwABaOzPYYwphyTTb5rNLgb8wlwcDSRAQRvJnb4a4TNZ4N/r+h2RCAKYD4C5gLAuGDEG0aUTCpoEj3mICFWYPoF5gKggmByBiYGoGINABi9vBSNASIBAv/7EMQCgAT8O0gZo4AAkIWgw55gABgvCwoyCJ4VwfJlrDFjJTb9yIpZxwdjebyZAERFmYVBX5Y8LMADgBUMY7RNTxE+AWi8k+E2Mp+nUNVupIgEJCQNQaPfWd4iTEFNRTMuMTAwqqqq";
        private static void TestResume()
        {
            string folder = Path.Combine(AppPaths.AppFolder, "Long speech fixtures"); Directory.CreateDirectory(folder);
            var request = new SpeechProject { VoiceId = "test", Text = "First sentence. Second sentence. Third sentence.", Variations = 2, OutputFormat = "pcm_44100" };
            var expected = LongSpeech.Split(request.Text, 20); var sent = new List<string>(); int calls = 0;
            Func<SpeechProject, Dictionary<string, object>, string, CancellationToken, ApiResult> fail = (part, context, path, token) =>
            {
                calls++; if (calls == 2) throw new IOException("Fixture interrupted");
                sent.Add(part.Text); Check(part.OutputFormat == "pcm_44100", "Long job did not request lossless PCM"); return WritePart(path, calls);
            };
            try
            {
                try { LongSpeech.Generate(request, null, folder, "Fixture", 20, CancellationToken.None, message => { }, fail); throw new Exception("Failure was hidden"); }
                catch (IOException) { checks++; }
                Check(Directory.GetFiles(folder, "v1-part1.wav", SearchOption.AllDirectories).Length == 1, "Completed part was not retained");
                calls = 1;
                var outputs = LongSpeech.Generate(request, null, folder, "Fixture", 20, CancellationToken.None, message => { }, (part, context, path, token) => { sent.Add(part.Text); return WritePart(path, ++calls); });
                Check(outputs.Count == 2 && string.Concat(sent) == request.Text + request.Text, "Resume regenerated a completed passage or missed a variation");
                using (var wave = new WaveFileReader(outputs[0].File))
                {
                    var data = new byte[(int)wave.Length]; wave.Read(data, 0, data.Length);
                    var wanted = Enumerable.Range(1, expected.Count).SelectMany(i => new byte[] { (byte)i, 0, (byte)i, 0 }).ToArray();
                    Check(data.SequenceEqual(wanted) && wave.WaveFormat.SampleRate == 44100, "Assembly re-encoded or changed PCM data");
                }
                Check(!Directory.GetFiles(folder, "Progress.json", SearchOption.AllDirectories).Any() && !Directory.GetFiles(folder, "*.partial", SearchOption.AllDirectories).Any(), "Completed job left checkpoints or partials");
                // Cancellation preserves finished parts without sending subsequent requests.
                using (var cancellation = new CancellationTokenSource())
                {
                    var changed = SpeechProject.FromJson(JsonData.Encode(request)); changed.Text += " Another sentence.";
                    try
                    {
                        LongSpeech.Generate(changed, null, folder, "Cancelled", 20, cancellation.Token, message => { }, (part, context, path, token) => { var result = WritePart(path, 1); cancellation.Cancel(); return result; });
                        throw new Exception("Cancellation was ignored");
                    }
                    catch (OperationCanceledException) { checks++; }
                    var partPath = Directory.GetFiles(folder, "v1-part1.wav", SearchOption.AllDirectories).Single();
                    File.AppendAllText(partPath, "tampered"); int attempts = 0;
                    Reject(() => LongSpeech.Generate(changed, null, folder, "Cancelled", 20, CancellationToken.None, message => { }, (part, context, path, token) => { attempts++; return WritePart(path, 1); }), "Changed checkpoint audio was reused");
                    Check(attempts == 0, "A paid request occurred before checkpoint validation");
                }
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
