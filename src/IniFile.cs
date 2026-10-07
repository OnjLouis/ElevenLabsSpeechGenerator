using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            if (!File.Exists(path)) return ini;
            var section = "General";
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                ini.Set(section, line.Substring(0, equals).Trim(), line.Substring(equals + 1).Trim());
            }
            return ini;
        }

        public string Get(string section, string key, string defaultValue)
        {
            Dictionary<string, string> values;
            string value;
            return sections.TryGetValue(section, out values) && values.TryGetValue(key, out value) ? value : defaultValue;
        }

        public void Set(string section, string key, string value)
        {
            Dictionary<string, string> values;
            if (!sections.TryGetValue(section, out values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[section] = values;
            }
            values[key] = value ?? string.Empty;
        }

        public void Save(string path, IEnumerable<string> comments)
        {
            var builder = new StringBuilder();
            foreach (var comment in comments ?? Enumerable.Empty<string>()) builder.AppendLine("; " + comment);
            foreach (var section in sections.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.AppendLine("[" + section.Key + "]");
                foreach (var pair in section.Value.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)) builder.AppendLine(pair.Key + "=" + pair.Value);
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }
    }
}
