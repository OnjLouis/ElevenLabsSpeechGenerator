using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace ElevenLabsSpeechGenerator
{
    internal static class Transcript
    {
        public static string Subtitles(Dictionary<string, object> data, bool separateWords = false)
        {
            var result = new StringBuilder(); var words = new StringBuilder(); double start = 0, end = 0; int number = 0;
            foreach (var word in JsonData.Items(data, "words"))
            {
                double a, b; if (!double.TryParse(JsonData.String(word, "start"), NumberStyles.Float, CultureInfo.InvariantCulture, out a) || !double.TryParse(JsonData.String(word, "end"), NumberStyles.Float, CultureInfo.InvariantCulture, out b) || double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b) || a < 0 || b < a || b > TimeSpan.MaxValue.TotalSeconds - 1) continue;
                if (words.Length == 0) start = a; else if (separateWords) words.Append(' '); words.Append(JsonData.String(word, "text")); end = b;
                if (words.Length >= 70 || end - start >= 5) { Cue(result, ++number, start, end, words.ToString()); words.Clear(); }
            }
            if (words.Length > 0) Cue(result, ++number, start, end, words.ToString()); return result.ToString();
        }
        private static void Cue(StringBuilder output, int number, double start, double end, string text)
        {
            output.Append(number).Append("\r\n").Append(Time(start)).Append(" --> ").Append(Time(Math.Max(end, start + .1))).Append("\r\n").Append(text.Trim()).Append("\r\n\r\n");
        }
        private static string Time(double seconds) { var t = TimeSpan.FromSeconds(seconds); return ((int)t.TotalHours).ToString("00") + t.ToString(@"\:mm\:ss\,fff", CultureInfo.InvariantCulture); }
    }
}
