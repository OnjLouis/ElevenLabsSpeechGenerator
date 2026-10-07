using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ElevenLabsSpeechGenerator
{
    internal enum SpeechMode { TextToSpeech = 0, Dialogue = 1, Transcription = 2, VoiceChanger = 3, VoiceDesign = 5, VoiceIsolation = 6, ForcedAlignment = 7, VoiceRemix = 8 }

    internal sealed class NamedItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Dictionary<string, object> Data { get; set; }
        public override string ToString() { return Name; }
    }

    internal sealed class DialogueLine
    {
        public string VoiceId { get; set; }
        public string VoiceName { get; set; }
        public string Text { get; set; }
        public override string ToString() { return VoiceName + ": " + (Text ?? "").Replace("\r", " ").Replace("\n", " "); }
    }

    internal sealed class SpeechProject
    {
        public int SchemaVersion { get; set; }
        public SpeechMode Mode { get; set; }
        public string Text { get; set; }
        public string Filename { get; set; }
        public string InputFile { get; set; }
        public string VoiceId { get; set; }
        public string ModelId { get; set; }
        public string OutputFormat { get; set; }
        public int Variations { get; set; }
        public double Stability { get; set; }
        public double Similarity { get; set; }
        public double Style { get; set; }
        public double Speed { get; set; }
        public bool SpeakerBoost { get; set; }
        public string Language { get; set; }
        public bool Diarize { get; set; }
        public bool AudioEvents { get; set; }
        public bool RemoveNoise { get; set; }
        public List<DialogueLine> Dialogue { get; set; }
        public List<Dictionary<string, object>> Dictionaries { get; set; }

        public SpeechProject()
        {
            SchemaVersion = 1; Text = Filename = InputFile = VoiceId = Language = "";
            ModelId = "eleven_v4"; OutputFormat = "mp3_44100_128"; Variations = 1;
            Stability = .5; Similarity = .75; Style = 0; Speed = 1; SpeakerBoost = true;
            AudioEvents = true;
            Dialogue = new List<DialogueLine>(); Dictionaries = new List<Dictionary<string, object>>();
        }
        public static SpeechProject ForMode(SpeechMode mode)
        {
            var project = new SpeechProject { Mode = mode };
            switch (mode)
            {
                case SpeechMode.Transcription: project.ModelId = "scribe_v2"; break;
                case SpeechMode.VoiceChanger: project.ModelId = "eleven_multilingual_sts_v2"; break;
                case SpeechMode.VoiceDesign: project.ModelId = "eleven_ttv_v3"; break;
                case SpeechMode.VoiceIsolation: project.ModelId = "audio_isolation"; break;
                case SpeechMode.ForcedAlignment: project.ModelId = "forced_alignment"; break;
                case SpeechMode.VoiceRemix: project.ModelId = "eleven_ttv_v3"; break;
            }
            return project;
        }

        public void Validate(int textLimit)
        {
            ValidateStructure();
            if (SchemaVersion != 1) throw new InvalidDataException("This project uses an unsupported format version.");
            if (!Enum.IsDefined(typeof(SpeechMode), Mode)) throw new InvalidDataException("Unknown generation mode.");
            if (Variations < 1 || Variations > 10) throw new InvalidDataException("Choose between 1 and 10 variations.");
            if (Dictionaries == null || Dictionaries.Count > 3) throw new InvalidDataException("Choose no more than three pronunciation dictionaries.");
            foreach (var d in Dictionaries) if (string.IsNullOrEmpty(JsonData.String(d, "pronunciation_dictionary_id")) || string.IsNullOrEmpty(JsonData.String(d, "version_id"))) throw new InvalidDataException("A pronunciation dictionary is missing its ID or version.");
            if (Stability < 0 || Stability > 1 || Similarity < 0 || Similarity > 1 || Style < 0 || Style > 1 || Speed < .25 || Speed > 4) throw new InvalidDataException("Voice settings are outside the supported range.");
            if (!OutputFormats.Contains(OutputFormat)) throw new InvalidDataException("Unsupported output format.");
            if (string.IsNullOrWhiteSpace(ModelId)) throw new InvalidDataException("Choose a model.");
            if ((Mode == SpeechMode.TextToSpeech || Mode == SpeechMode.VoiceChanger || Mode == SpeechMode.VoiceRemix) && string.IsNullOrWhiteSpace(VoiceId)) throw new InvalidDataException("Choose a voice.");
            if (Mode == SpeechMode.Transcription || Mode == SpeechMode.VoiceChanger || Mode == SpeechMode.VoiceIsolation || Mode == SpeechMode.ForcedAlignment)
            {
                if (!File.Exists(InputFile)) throw new InvalidDataException("Select an existing audio file.");
                if (new FileInfo(InputFile).Length == 0) throw new InvalidDataException("Select an audio file that is not empty.");
                if (new FileInfo(InputFile).Length > 500L * 1024 * 1024) throw new InvalidDataException("Select an audio file smaller than 500 MB.");
                if (Mode == SpeechMode.ForcedAlignment && (string.IsNullOrWhiteSpace(Text) || Text.Length > 675000)) throw new InvalidDataException("Enter the matching transcript, up to 675,000 characters.");
            }
            else if (Mode == SpeechMode.Dialogue)
            {
                if (Dialogue == null || Dialogue.Count == 0 || Dialogue.Any(x => string.IsNullOrWhiteSpace(x.VoiceId) || string.IsNullOrWhiteSpace(x.Text))) throw new InvalidDataException("Add dialogue lines with a voice and text.");
                if (Dialogue.Sum(x => TextLength(x.Text)) > 2000 || Dialogue.Select(x => x.VoiceId).Distinct().Count() > 10) throw new InvalidDataException("Dialogue accepts up to 2,000 characters and 10 different voices.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Text)) throw new InvalidDataException("Enter some text first.");
                int limit = Mode == SpeechMode.VoiceDesign || Mode == SpeechMode.VoiceRemix ? 1000 : textLimit;
                if (TextLength(Text) > limit) throw new InvalidDataException("This model accepts up to " + limit + " characters.");
                if (Mode == SpeechMode.VoiceDesign && TextLength(Text) < 20) throw new InvalidDataException("Describe the voice in at least 20 characters.");
                if (Mode == SpeechMode.VoiceRemix && TextLength(Text) < 5) throw new InvalidDataException("Describe the voice changes in at least five characters.");
            }
        }

        public void ValidateStructure()
        {
            if (SchemaVersion != 1 || !Enum.IsDefined(typeof(SpeechMode), Mode)) throw new InvalidDataException("This is not a supported speech project.");
            if (Text == null || Filename == null || InputFile == null || VoiceId == null || ModelId == null || Language == null || Dialogue == null || Dialogue.Any(x => x == null || x.Text == null || x.VoiceId == null) || Dictionaries == null || Dictionaries.Count > 3 || Dictionaries.Any(x => x == null)) throw new InvalidDataException("The project contains missing or invalid fields.");
            if (new[] { Stability, Similarity, Style, Speed }.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new InvalidDataException("The project contains an invalid numeric setting.");
            if (Variations < 1 || Variations > 10 || Stability < 0 || Stability > 1 || Similarity < 0 || Similarity > 1 || Style < 0 || Style > 1 || Speed < .25 || Speed > 4 || !OutputFormats.Contains(OutputFormat)) throw new InvalidDataException("The project contains unsupported settings.");
            if ((ModelId == "eleven_v3" || Mode == SpeechMode.Dialogue) && Stability != 0 && Stability != .5 && Stability != 1) throw new InvalidDataException("This model needs stability of 0, 50, or 100 percent.");
        }

        public Dictionary<string, object> JsonBody(NamedItem model)
        {
            var body = new Dictionary<string, object>();
            if (Mode == SpeechMode.VoiceDesign || Mode == SpeechMode.VoiceRemix)
            {
                body["voice_description"] = Text; body["auto_generate_text"] = true; if (Mode == SpeechMode.VoiceDesign) body["model_id"] = ModelId;
                return body;
            }
            body["model_id"] = ModelId;
            if (Mode == SpeechMode.Dialogue)
            {
                body["inputs"] = Dialogue.Select(x => new Dictionary<string, object> { { "text", x.Text }, { "voice_id", x.VoiceId } }).ToArray();
                body["settings"] = new Dictionary<string, object> { { "stability", Stability } };
            }
            else { body["text"] = Text; body["voice_settings"] = VoiceSettings(model); }
            if (!string.IsNullOrWhiteSpace(Language)) body["language_code"] = Language.Trim();
            if (Dictionaries.Count > 0) body["pronunciation_dictionary_locators"] = Dictionaries;
            return body;
        }

        public Dictionary<string, object> VoiceSettings(NamedItem model)
        {
            var result = new Dictionary<string, object> { { "stability", Stability }, { "similarity_boost", Similarity } };
            if (!ModelId.StartsWith("eleven_v4", StringComparison.Ordinal)) result["speed"] = Speed;
            if (!ModelId.StartsWith("eleven_v4", StringComparison.Ordinal) && model != null && JsonData.Bool(model.Data, "can_use_style")) result["style"] = Style;
            if (!ModelId.StartsWith("eleven_v4", StringComparison.Ordinal) && model != null && JsonData.Bool(model.Data, "can_use_speaker_boost")) result["use_speaker_boost"] = SpeakerBoost;
            return result;
        }

        public static readonly string[] OutputFormats = { "mp3_44100_128", "mp3_44100_192", "pcm_16000", "pcm_22050", "pcm_24000", "pcm_44100", "pcm_48000" };
        public static SpeechProject FromJson(string text)
        {
            var data = JsonData.Object(text); object nested;
            if (data == null) throw new InvalidDataException("This is not a speech project.");
            if (data.TryGetValue("project", out nested)) data = nested as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("SchemaVersion") || !data.ContainsKey("Mode")) throw new InvalidDataException("This file does not contain a reusable speech project.");
            var project = JsonData.Serializer().Deserialize<SpeechProject>(JsonData.Encode(data)); project.ValidateStructure(); return project;
        }
        public static int TextLength(string value) { return (value ?? "").Length; }
        public static string WindowsLines(string value) { return (value ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
    }

    internal static class JsonData
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 100 }; }
        public static string Encode(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Object(string text) { return Serializer().Deserialize<Dictionary<string, object>>(text); }
        public static string String(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        public static bool Bool(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
        public static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v)) return new Dictionary<string, object>[0];
            var a = v as System.Collections.IEnumerable; return a == null ? new Dictionary<string, object>[0] : a.Cast<object>().OfType<Dictionary<string, object>>();
        }
    }

    internal static class FileNames
    {
        internal static string GenerationFolder(string root, SpeechMode mode, string voiceName)
        {
            if (mode == SpeechMode.Transcription || mode == SpeechMode.ForcedAlignment) return root;
            return Path.Combine(root, Stem(mode == SpeechMode.Dialogue ? "Dialogue" : mode == SpeechMode.VoiceDesign ? "Voice design" : mode == SpeechMode.VoiceRemix ? "Voice remix" : mode == SpeechMode.VoiceIsolation ? "Voice isolation" : voiceName));
        }
        public static string Stem(string text)
        {
            var s = new string((text ?? "").Select(c => c < 32 || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (s.Length > 100) s = s.Substring(0, 100).TrimEnd();
            if (s.Length > 0 && char.IsHighSurrogate(s[s.Length - 1])) s = s.Substring(0, s.Length - 1);
            if (s.Length == 0) s = "Speech";
            var first = s.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(first)) s = "_" + s;
            return s;
        }
        public static string Next(string folder, string stem, string extension)
        {
            var path = Path.Combine(folder, Stem(stem) + extension); int n = 2;
            while (File.Exists(path)) path = Path.Combine(folder, Stem(stem) + " (" + n++ + ")" + extension);
            return path;
        }
        public static void AtomicText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
