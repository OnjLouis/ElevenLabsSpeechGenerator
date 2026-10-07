using System;
namespace ElevenLabsSpeechGenerator
{
    internal static class VoiceGroups
    {
        public static readonly string[] Names = { "All voices", "Your voices", "Cloned voices", "Designed voices", "Default voices", "Shared voices" };
        public static bool Matches(NamedItem voice, string group)
        {
            var category = JsonData.String(voice.Data, "category");
            bool owner = JsonData.Bool(voice.Data, "is_owner");
            switch (group)
            {
                case "Your voices": return owner && category != "premade";
                case "Cloned voices": return category == "cloned" || category == "professional";
                case "Designed voices": return category == "generated";
                case "Default voices": return category == "premade";
                case "Shared voices": return voice.Data.ContainsKey("is_owner") && !owner && category != "premade";
                default: return true;
            }
        }
    }
}
