using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("ElevenLabs Speech Generator")]
[assembly: AssemblyDescription("Accessible portable ElevenLabs speech, dialogue, transcription and voice utility")]
[assembly: AssemblyCompany("Andre Louis")]
[assembly: AssemblyProduct("ElevenLabs Speech Generator")]
[assembly: AssemblyCopyright("Copyright 2026 Andre Louis")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion(ElevenLabsSpeechGenerator.AppVersion.Full)]
[assembly: AssemblyFileVersion(ElevenLabsSpeechGenerator.AppVersion.Full)]

namespace ElevenLabsSpeechGenerator
{
    internal static class AppVersion
    {
        public const string Short = "1.1.0";
        public const string Full = Short + ".0";
    }
}
