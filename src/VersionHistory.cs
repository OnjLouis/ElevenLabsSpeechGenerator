using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class VersionHistory
    {
        internal static readonly string Text = "1.2.2" + Environment.NewLine + Environment.NewLine +
            "Generation History now displays voice names and full transcripts for current speech and dialogue recordings. Search includes the full transcript on the current page. History playback and downloads retrieve playable WAV files, including recordings originally generated as raw PCM. Windows playback errors appear above the active dialog." + Environment.NewLine + Environment.NewLine +
            "1.2.1" + Environment.NewLine + Environment.NewLine +
            "Generation History, Voice Library and pronunciation dictionaries now offer Previous Page as well as Next Page. Navigation remains usable after failed requests; Refresh returns to the first page." + Environment.NewLine + Environment.NewLine +
            "1.2.0" + Environment.NewLine +
            "" + Environment.NewLine +
            "- Voice Library can automatically preview selected voices; Enter or Preview / Stop toggles playback." + Environment.NewLine +
            "- Rename voices owned by your account without changing their IDs or recordings." + Environment.NewLine +
            "- Expanded the speech-tag picker with current official examples." + Environment.NewLine +
            "- Voice dropdowns and the speech-tag list now accept multi-letter typing navigation." + Environment.NewLine +
            "" + Environment.NewLine +
            "1.1.1" + Environment.NewLine +
            "" + Environment.NewLine +
            "Version History is now available from the Help menu, including when offline.";

        internal static ApiKeyTestResultForm CreateDialog()
        {
            return new ApiKeyTestResultForm(Text, "Version history") { Size = new Size(720, 480) };
        }

        internal static void Show(IWin32Window owner)
        {
            using (var dialog = CreateDialog()) dialog.ShowDialog(owner);
        }
    }
}
