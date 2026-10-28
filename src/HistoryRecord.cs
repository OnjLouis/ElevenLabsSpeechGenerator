using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace ElevenLabsSpeechGenerator
{
    internal static class HistoryRecord
    {
        private static List<Dictionary<string, object>> Dialogue(Dictionary<string, object> data)
        {
            return JsonData.Items(data, "dialogue").Where(x => !string.IsNullOrWhiteSpace(JsonData.String(x, "text"))).ToList();
        }
        private static string Text(Dictionary<string, object> data)
        {
            var text = JsonData.String(data, "text");
            return !string.IsNullOrWhiteSpace(text) ? text : string.Join("\n", Dialogue(data).Select(x => JsonData.String(x, "text")));
        }
        private static string SingleLine(string text)
        {
            return string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
        private static string Kind(Dictionary<string, object> data)
        {
            switch (JsonData.String(data, "source")) {
                case "TTS": return "Speech";
                case "STS": return "Voice conversion";
                case "Dubbing": return "Dubbing";
                case "VoicePreview": return "Voice preview";
                case "ConvAI": return "Conversation";
                default: return "Recording";
            }
        }
        private static string Date(Dictionary<string, object> data)
        {
            double seconds;
            if (!double.TryParse(JsonData.String(data, "date_unix"), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return "";
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime().ToString("d MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture); }
            catch (ArgumentOutOfRangeException) { return ""; }
        }
        public static string Label(Dictionary<string, object> data)
        {
            var voice = SingleLine(JsonData.String(data, "voice_name"));
            if (voice.Length == 0) voice = string.Join(", ", Dialogue(data).Select(x => SingleLine(JsonData.String(x, "voice_name"))).Where(x => x.Length > 0).Distinct());
            var text = SingleLine(Text(data));
            if (text.Length > 160) { text = text.Substring(0, 160); if (char.IsHighSurrogate(text[text.Length - 1])) text = text.Substring(0, text.Length - 1); text += "..."; }
            if (text.Length > 0) return voice.Length > 0 ? voice + " - " + text : text;
            var date = Date(data);
            var suffix = date.Length > 0 ? date : JsonData.String(data, "history_item_id");
            return (voice.Length > 0 ? voice : Kind(data)) + (suffix.Length > 0 ? " - " + suffix : "");
        }
        public static string Details(Dictionary<string, object> data)
        {
            // New history responses put even single-speaker speech in dialogue.
            var text = JsonData.String(data, "text");
            if (string.IsNullOrWhiteSpace(text)) text = string.Join("\n\n", Dialogue(data).Select(x => {
                var voice = JsonData.String(x, "voice_name").Trim();
                return (voice.Length > 0 ? voice + ": " : "") + JsonData.String(x, "text");
            }));
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(text)) lines.Add(text);
            else lines.Add("No transcript is available for this recording.");
            if (Date(data).Length > 0) lines.Add("Created: " + Date(data));
            if (JsonData.String(data, "model_id").Length > 0) lines.Add("Model: " + JsonData.String(data, "model_id"));
            return SpeechProject.WindowsLines(string.Join("\n", lines));
        }
        public static bool Matches(Dictionary<string, object> data, string query)
        {
            return (Label(data) + "\n" + Details(data)).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
}
