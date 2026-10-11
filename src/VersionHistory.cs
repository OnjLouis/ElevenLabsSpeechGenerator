using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class VersionHistory
    {
        internal static readonly string Text = "1.1.1" + Environment.NewLine +
            "" + Environment.NewLine +
            "Version History is now available from the Help menu, including when offline." + Environment.NewLine +
            "" + Environment.NewLine +
            "1.1.0" + Environment.NewLine +
            "" + Environment.NewLine +
            "- Optional long-text speech generation uses sentence-aware splitting, assembles one WAV per variation and can recover completed parts." + Environment.NewLine +
            "- Voice cloning shows readable upload progress and the final result." + Environment.NewLine +
            "- Windows context help provides specific explanations for cloning controls, Preferences and other dialogs." + Environment.NewLine +
            "- The Mac variations field accepts a typed number.";

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
