using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ElevenLabsSpeechGenerator
{
    internal static class VoiceNames
    {
        internal static bool CanRename(NamedItem voice)
        {
            return voice != null && JsonData.Bool(voice.Data, "is_owner") &&
                !string.Equals(JsonData.String(voice.Data, "category"), "premade", StringComparison.OrdinalIgnoreCase);
        }
        internal static string Validate(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Any(char.IsControl)) throw new InvalidDataException("Enter a voice name without line breaks.");
            return name;
        }
        internal static void Rename(SpeechClient client, NamedItem voice, string name, CancellationToken token)
        {
            if (!CanRename(voice)) throw new InvalidDataException("Only voices owned by your account can be renamed.");
            name = Validate(name);
            var response = JsonData.Object(client.Upload("/v1/voices/" + Uri.EscapeDataString(voice.Id) + "/edit",
                new Dictionary<string, string> { { "name", name } }, new List<UploadFile>(), token));
            if (JsonData.String(response, "status") != "ok") throw new InvalidDataException("ElevenLabs did not confirm the rename. Refresh the voice library before trying again.");
        }
    }
}
