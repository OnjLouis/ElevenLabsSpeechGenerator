using System;
using System.Globalization;
using System.IO;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class AppSettings
    {
        public string DefaultOutputFolder = AppPaths.DefaultAudioFolder;
        public string UpdateCheckFrequency = "Startup";
        public bool InstallUpdatesSilently;
        public DateTime LastUpdateCheckUtc = DateTime.MinValue;
        public bool CompletionSound = true;
        public int PlaybackDevice = -1;
        public bool AutoPlayGenerations;
        public string DefaultOutputFormat = "mp3_44100_128";
        public string VoiceGroup = "All voices";
        public bool SaveDetails = true;
        public bool AllowLongSpeech;
        public static AppSettings Load()
        {
            var s = new AppSettings(); var ini = IniFile.Load(AppPaths.SettingsPath);
            s.VoiceGroup = ini.Get("General", "VoiceGroup", "All voices");
            if (Array.IndexOf(VoiceGroups.Names, s.VoiceGroup) < 0) s.VoiceGroup = "All voices";
            var folder = ini.Get("General", "OutputFolder", @".\Audio");
            s.DefaultOutputFolder = ResolveOutputFolder(folder, false);
            s.UpdateCheckFrequency = NormalizeUpdateFrequency(ini.Get("Updates", "CheckFrequency", "Startup"));
            bool b; int n; DateTime d;
            if (bool.TryParse(ini.Get("Updates", "InstallSilently", "false"), out b)) s.InstallUpdatesSilently = b;
            if (bool.TryParse(ini.Get("General", "CompletionSound", "true"), out b)) s.CompletionSound = b;
            if (bool.TryParse(ini.Get("General", "SaveDetails", "true"), out b)) s.SaveDetails = b;
            if (bool.TryParse(ini.Get("General", "AllowLongSpeech", "false"), out b)) s.AllowLongSpeech = b;
            if (int.TryParse(ini.Get("General", "PlaybackDevice", "-1"), out n)) s.PlaybackDevice = n;
            if (bool.TryParse(ini.Get("Audio", "AutoPlayGenerations", "false"), out b)) s.AutoPlayGenerations = b;
            var format = ini.Get("Audio", "DefaultOutputFormat", s.DefaultOutputFormat);
            if (Array.IndexOf(SpeechProject.OutputFormats, format) >= 0) s.DefaultOutputFormat = format;
            if (DateTime.TryParse(ini.Get("Updates", "LastCheckUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) s.LastUpdateCheckUtc = d;
            return s;
        }
        public static string NormalizeFolderInput(string value)
        {
            var folder = (value ?? string.Empty).Trim();
            if (folder.Length >= 2 && folder[0] == '"' && folder[folder.Length - 1] == '"')
                folder = folder.Substring(1, folder.Length - 2);
            return Environment.ExpandEnvironmentVariables(folder);
        }

        public static string ResolveOutputFolder(string value, bool requireAbsolute)
        {
            var folder = NormalizeFolderInput(value);
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Choose a default output folder.");
            try
            {
                var root = Path.GetPathRoot(folder);
                if (requireAbsolute && (!Path.IsPathRooted(folder) || root == @"\" || root.EndsWith(":", StringComparison.Ordinal)))
                    throw new InvalidDataException("Choose an absolute default output folder, such as C:\\Audio.");
                return Path.GetFullPath(requireAbsolute ? folder : Path.Combine(AppPaths.AppFolder, folder));
            }
            catch (ArgumentException ex) { throw new InvalidDataException("The default output folder contains invalid path characters.", ex); }
            catch (NotSupportedException ex) { throw new InvalidDataException("The default output folder is not a supported folder path.", ex); }
            catch (PathTooLongException ex) { throw new InvalidDataException("The default output folder path is too long.", ex); }
        }

        public void Save()
        {
            var ini = new IniFile(); var folder = DefaultOutputFolder;
            ini.Set("Audio", "AutoPlayGenerations", AutoPlayGenerations.ToString());
            ini.Set("Audio", "DefaultOutputFormat", DefaultOutputFormat);
            ini.Set("General", "VoiceGroup", VoiceGroup);
            ini.Set("General", "AllowLongSpeech", AllowLongSpeech.ToString());
            if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(AppPaths.DefaultAudioFolder), StringComparison.OrdinalIgnoreCase)) folder = @".\Audio";
            ini.Set("General", "OutputFolder", folder); ini.Set("General", "CompletionSound", CompletionSound.ToString()); ini.Set("General", "SaveDetails", SaveDetails.ToString()); ini.Set("General", "PlaybackDevice", PlaybackDevice.ToString(CultureInfo.InvariantCulture));
            ini.Set("Updates", "CheckFrequency", NormalizeUpdateFrequency(UpdateCheckFrequency)); ini.Set("Updates", "InstallSilently", InstallUpdatesSilently.ToString()); ini.Set("Updates", "LastCheckUtc", LastUpdateCheckUtc.ToString("o", CultureInfo.InvariantCulture));
            ini.Save(AppPaths.SettingsPath, new[] { "ElevenLabs Speech Generator settings", "API keys are protected separately." });
        }
        public static string NormalizeUpdateFrequency(string value) { foreach (var s in new[] { "Never", "Daily", "Weekly" }) if (string.Equals(value, s, StringComparison.OrdinalIgnoreCase)) return s; return "Startup"; }
    }
}
