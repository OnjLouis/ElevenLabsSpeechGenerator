using System;
using System.Collections.Generic;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class ContextHelp : IMessageFilter, IDisposable
    {
        private readonly Form main;
        private readonly Action manual;
        private readonly Func<Control, string> describe;
        private bool disposed;
        private const int KeyDownMessage = 0x0100;
        private static readonly Dictionary<string, string> Instructions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Clone voice/Voice description", "Optional description of the cloned voice. This does not change how the samples sound." },
            { "Add audio files", "Choose recordings to include as voice-cloning samples. You can select several files; selecting them does not upload them yet." },
            { "Remove sample", "Remove the selected recording from the upload list without deleting the original file." },
            { "I have the rights and consent to clone this voice", "Confirm that the speaker has given permission and that you have the rights to use these recordings for voice cloning." },
            { "Create voice", "Upload the selected samples and create a cloned voice in your account after confirmation. Clone status reports upload progress and the result." },
            { "Cancel upload", "Stop the active cloning request. If the upload has already reached ElevenLabs, the voice may still be created; check your voice library before retrying." },
            { "Add line", "Add a dialogue line using the selected voice." },
            { "Remove", "Remove the selected dialogue line. Other lines are kept; the changes are applied when you choose OK." },
            { "Move up", "Move the selected dialogue line earlier in the conversation." },
            { "Move down", "Move the selected dialogue line later in the conversation." },
            { "Insert tag", "Choose a delivery instruction to insert at the saved cursor position in this dialogue line, without replacing selected text." },
            { "Browse", "Choose the default folder for completed recordings and transcripts." },
            { "Automatic dubbing/Browse", "Choose the local audio or video file to translate. Selecting it does not upload it or start dubbing." },
            { "Choose audio", "Choose a recording for the current operation. Selecting it does not upload it or start generation." },
            { "Add preview voice", "Save the selected designed or remixed voice preview as a new voice in your account after confirmation." },
            { "Create", "Create a pronunciation dictionary in your account, starting with its name and first rule." },
            { "Dictionary name", "Name for the pronunciation dictionary being created in your account." },
            { "Download", "Download the selected history recording to a local file. This retrieves existing audio without generating it again." },
            { "Generation history/Play", "Download and play the selected history recording without generating it again." },
            { "Generation history/Items", "Choose an existing account recording to download or play." },
            { "Voice library/Items", "Choose a voice to preview or use. Delete Voice removes an account voice only after confirmation." },
            { "Pronunciation dictionaries/Items", "Choose up to three pronunciation dictionaries to apply to speech, or select one to export or add a rule." },
            { "Allow long speech text", "Allow up to 500,000 characters in Text to speech. Split at paragraph, sentence or word boundaries and assemble one WAV per variation. Each part is a separate paid request; completed parts can be reused when resuming the identical request." },
            { "Open manual", "Open the complete application guide in your browser." },
            { "Mode", "Choose the operation. Each mode keeps its own draft." },
            { "Voice", "Choose the voice used for speech or voice conversion. Refresh voices reloads the account choices." },
            { "Voice group", "Filter the account's loaded voices. Your Voices uses account ownership, Cloned includes instant and professional clones, Designed shows generated voices, Default shows premade voices, and Shared shows voices owned by others. The current voice stays selectable with an outside-group label so filtering never silently changes it." },
            { "Model", "Choose the generation model. Available options and limits depend on the selected mode and your account." },
            { "Speech text", "Enter the words to be spoken. Generate submits the text using the selected voice and model." },
            { "Music prompt", "Describe the music you want. A composition plan gives more detailed control over its sections." },
            { "Sound effects prompt", "Describe the sound you want. Automatic duration lets the service choose its length." },
            { "Dialogue summary", "Review the dialogue. Use Ctrl+P to edit its lines and voices while Dialogue mode is selected." },
            { "Transcript", "Review or copy the returned transcription. Use transcript as speech starts a separate speech draft." },
            { "Voice description", "Describe the voice you want to design. Generation returns previews; saving an account voice is a separate confirmed action." },
            { "Voice changes", "Describe how to change the selected voice in 5 to 1,000 characters. Remix returns previews without changing the original account voice. Saving a preview as a new voice requires confirmation." },
            { "Matching transcript", "Enter the words already spoken in the selected recording. Alignment returns their timing for subtitles; it does not transcribe or translate the recording." },
            { "Dubbing source", "Choose a local audio or video file, or a public HTTPS URL. Starting a new job uploads the source after confirmation and may spend credits." },
            { "Source audio or video", "The recording to translate. Local files must be no larger than 500 MB. A public URL must use HTTPS without embedded credentials." },
            { "Dubbing job name", "Optional project and output name. Leave blank to use the source name." },
            { "Source language", "Choose the original language, automatic detection, or type a supported language code." },
            { "Target language", "Choose the translation language or type its supported language code. A saved job keeps its original target." },
            { "Dubbing status", "Shows progress, saved project information and errors. Resume checks the saved project without submitting another paid job." },
            { "Start dubbing", "Create a paid dubbing project after confirmation. Stopping local monitoring does not cancel an accepted project or its charge." },
            { "Resume job", "Check the saved project and download its completed output without creating another paid project." },
            { "Stop waiting", "Stop local monitoring. The saved job can be resumed; ElevenLabs may continue processing and charge for it." },
            { "Open job", "Open a saved dubbing job to resume checking its progress or download its output." },
            { "New job", "Prepare a separate dubbing job without deleting existing local or account projects." },
            { "Status log", "Shows generation progress and errors, newest last." },
            { "Credit balance", "Shows the last reported included allowance, usage and reset. Refresh balance obtains current account information; this does not control overage billing." },
            { "Base filename", "Name for the output files. Leave blank to use the beginning of your text. Existing recordings are preserved." },
            { "Output format", "Choose MP3 or a PCM WAV sample rate. Some formats require a higher subscription tier." },
            { "Variations", "Number of separate generations requested." },
            { "Number of variations", "Number of separate generations requested." },
            { "Default output folder", "Choose the folder where completed audio and transcripts are saved automatically." },
            { "API key", "An ElevenLabs key is a private credential that lets this app use your account. Create one on the ElevenLabs API keys page, allow the features you need, then paste it here and save Preferences. Test API Key checks Models, Voices and balance read access without generating audio. Never share the key; requests may spend your credits." },
            { "Generated files and voice previews", "Choose a completed result. Enter plays it; Escape stops playback. Save a Copy creates an additional copy elsewhere." },
            { "Dialogue lines", "Select the dialogue line to edit. Add, remove or reorder lines; OK accepts edits and Cancel leaves the dialogue unchanged." },
            { "Text for this line", "Words spoken by the selected dialogue voice." },
            { "Voice for this line", "Choose the voice that speaks this dialogue line." },
            { "Stability percent", "Controls consistency of delivery. Lower values allow more emotional variation but may produce uneven results; higher values are steadier and may sound less expressive. Enter a percentage or use the arrow keys. Eleven v3 and Dialogue use 0 for Creative, 50 for Natural, or 100 for Robust." },
            { "Similarity percent", "Controls how closely the result preserves the chosen voice's character. Enter a percentage or use the arrow keys. Higher is not always better: compare short generations if the result sounds unnatural." },
            { "Style percent", "Exaggerates the speaker's original style on supported models. Zero disables exaggeration. Higher values can reduce stability. Enter a percentage or use the arrow keys; unavailable models do not submit this setting." },
            { "Speed", "One is normal speech speed. Values below one slow speech down; values above one speed it up. Enter a value or use the arrow keys. This setting is unavailable where the chosen model does not support it." },
            { "Speaker boost", "Enhances similarity to the original speaker where supported. It may increase generation latency. It is not an audio volume control." },
            { "Search tags", "Filter documented examples and your saved custom tags. These are suggestions, not an exhaustive list of supported instructions." },
            { "Speech tags", "Choose a delivery instruction, then Insert places it before the saved cursor position without replacing your selected text. Use with Eleven v3 or v4. Results depend on the voice; Official Guide opens current guidance." },
            { "Custom tag", "Enter one delivery instruction, with or without square brackets. Save Custom Tag keeps it for reuse; Insert uses it immediately without saving. Tags are instructions before speech, not opening and closing markup." },
            { "Preference categories", "Choose General, API key, Updates or Audio, then move to that tab's controls." },
            { "Play a completion sound", "Play a short signal when generation finishes. Automatic audio playback replaces this signal when enabled." },
            { "Allow long speech text (split into parts)", "Allow up to 500,000 characters in Text to speech. The app splits at paragraph, sentence or word boundaries within the model's limit and assembles one WAV per variation. Each part is a separate paid request. Completed parts can be reused when you resume the identical request." },
            { "Clone status", "Shows upload progress, the wait for ElevenLabs to create the voice, and the final result." },
            { "Save generation details", "Save reusable request and project information as JSON in the output folder's Details subfolder. These files may contain your text but never your API key." },
            { "Check for updates", "Choose how often to check for a new version. Startup checks when the app opens; Never disables automatic checks. Manual checks remain available." },
            { "Install verified updates silently", "Install verified updates found by automatic checks without asking. Off by default. Your preferences, drafts and recordings are preserved." },
            { "Search", "Enter a name or words to find in this catalogue, then Refresh. Next Page loads more results where available." },
            { "Items", "Choose a voice, history recording or pronunciation dictionary. Details describes the selected item. Dictionary selection allows up to three items." },
            { "Details", "Read the selected catalogue item's description or recorded text. This is read-only." },
            { "Original text", "The word or phrase to pronounce differently. It is matched using the selected pronunciation dictionary." },
            { "Pronunciation", "For an alias, enter replacement words. For a phoneme rule, enter IPA or CMU phonemes supported by the selected model." },
            { "Rule type", "Alias substitutes written words. IPA and CMU supply phonemes; support depends on the speech model." },
            { "Voice name", "Choose a name for the voice being created in your account." },
            { "Voice samples", "Recordings to upload for voice cloning. Only use material for which you have the required rights and the speaker's consent." },
            { "Show API key", "Reveal or hide the credential in this dialog. Avoid revealing it during screen sharing." },
            { "Get an ElevenLabs API key", "Open ElevenLabs' API keys page in your browser. Sign in to your own account, create a key and grant only the permissions you need." },
            { "Close", "Close this dialog and return to its previous window." },
            { "OK", "Accept this dialog's settings or edits and return to its previous window." },
            { "Cancel", "Discard this dialog's unaccepted edits and return to its previous window." },
            { "Refresh", "Reload catalogue results using the current search and voice-library choice." },
            { "Next page", "Load another page of catalogue results, where available." },
            { "Search shared voice library", "Search publicly shared voices instead of your account list. Adding one to your account requires confirmation." },
            { "Use voice", "Select this voice for the current speech project. A shared-library voice is added to your account only after confirmation." },
            { "Preview", "Play a voice's existing preview without generating a new recording." },
            { "Delete voice", "Permanently remove the selected voice from your account after confirmation. This is not needed to remove a voice filter." },
            { "Use selected", "Use up to three selected pronunciation dictionaries with speech. This does not change your source text." },
            { "Import PLS", "Upload a pronunciation lexicon file to create a dictionary in your account." },
            { "Export PLS", "Save the selected pronunciation dictionary as a local lexicon file." },
            { "Add rule", "Add an alias or supported phoneme rule to the selected dictionary." },
            { "Insert", "Insert the tag before the saved cursor position without replacing selected text." },
            { "Save custom tag", "Keep this delivery instruction in your tag list for reuse." },
            { "Remove custom tag", "Remove the selected saved custom tag without changing speech text. Built-in examples remain available." },
            { "Official guide", "Open ElevenLabs' current prompting guidance in your browser. The local examples are not an exhaustive supported-tag list." },
            { "Length in seconds", "Requested duration. In music, a composition plan supplies section lengths; for sounds, automatic duration chooses the length." },
            { "Prompt influence", "Controls how closely a sound effect follows the description, from zero to one." },
            { "Lyrics and cues", "Words or performance cues for this composition-plan section. Enter starts a new line." },
            { "Include styles", "Styles to include for this section, one per line." },
            { "Exclude styles", "Styles to avoid for this section, one per line." },
            { "Playback device", "Choose the audio output used by this app. System default follows Windows." },
            { "Play new generations automatically in sequence", "Off by default. Play only the new audio from a successfully completed batch, one file at a time. Stop ends the queue; cancelled or failed batches do not start automatically." },
            { "Generated audio", "Choose a saved result. Enter plays it; Escape stops the current audio and the rest of the queue." },
            { "Generate", "Start the selected operation after confirmation. This sends your text or audio to ElevenLabs and may spend credits." },
            { "Voice settings", "Adjust the settings supported by the selected model." },
            { "Edit dialogue", "Edit dialogue lines and voices. Available only in Dialogue mode; it never switches modes." },
            { "Save a copy", "Save an extra copy of the selected result elsewhere. The original is already saved in the output folder." },
            { "Play", "Play the selected completed recording." },
            { "Preview voice", "Play the selected voice's existing preview recording without generating speech or spending credits. Unavailable if the voice has no preview. Control+Shift+Space starts it; Escape stops playback." },
            { "Default output format", "Choose the audio format for new projects. A saved project's format is retained; the main window can override it for a project." },
            { "Stop", "Stop audio playback." },
            { "Cancel generation", "Cancel the active request. Completed files are kept; an accepted request can still be charged." },
            { "Use composition plan", "Use section lengths, styles and lyrics from the composition plan instead of a single music description." },
            { "Instrumental", "Request music without vocals. A composition plan can specify its own vocal choices." },
            { "Automatic duration", "Let ElevenLabs choose a suitable sound-effect length, up to 30 seconds." },
            { "Loop sound effect", "Request a sound effect suitable for looping." },
            { "Refresh voices", "Reload the voices and models available to your account." },
            { "Test API key", "Check Models, Voices and balance read access. This test does not spend generation credits or prove permission for every generation mode." },
            { "Identify speakers", "Request speaker labels in transcription details." },
            { "Include audio events", "Include non-speech events such as laughter in the transcript." },
            { "Remove background noise", "Clean background noise before voice conversion." },
            { "Language code, blank for automatic", "Optional language code. Leave blank for automatic language detection." },
            { "Input audio file", "The recording selected for transcription, voice conversion, isolation or alignment. Choose Audio selects a file; selecting alone does not upload it." }
        };
        private ContextHelp(Form main, Action manual, Func<Control, string> describe)
        {
            this.main = main; this.manual = manual; this.describe = describe;
            Application.AddMessageFilter(this);
            main.Disposed += delegate { Dispose(); };
        }
        public static void Attach(Form main, Action manual, Func<Control, string> describe = null)
        {
            new ContextHelp(main, manual, describe ?? Description);
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || message.Msg != KeyDownMessage || (Keys)message.WParam.ToInt32() != Keys.F1 || Control.ModifierKeys != Keys.None) return false;
            var control = Control.FromChildHandle(message.HWnd);
            var owner = control == null ? null : control.FindForm();
            for (var form = owner; form != null; form = form.Owner)
            {
                if (form != main) continue;
                Show(owner, manual, describe);
                return true;
            }
            return false;
        }
        internal static Control FocusedControl(Control parent)
        {
            foreach (Control child in parent.Controls)
                if (child.ContainsFocus) return FocusedControl(child);
            return parent;
        }
        internal static string ControlName(Control control)
        {
            for (var current = control; current != null && !(current is Form); current = current.Parent)
            {
                if (!string.IsNullOrEmpty(current.AccessibleName)) return current.AccessibleName;
                if (current is ButtonBase || current is LinkLabel) return current.Text.Replace("&", "").Trim().TrimEnd('.', ':');
            }
            return "Current window";
        }
        internal static string Description(Control control)
        {
            string text;
            var name = ControlName(control);
            var form = control == null ? null : control.FindForm();
            if (form != null && Instructions.TryGetValue(form.Text + "/" + name, out text)) return text;
            if (Instructions.TryGetValue(name, out text)) return text;
            for (var current = control; current != null && !(current is Form); current = current.Parent)
                if (!string.IsNullOrEmpty(current.AccessibleDescription)) return current.AccessibleDescription;
            return "No additional description is available for " + name + ".";
        }
        internal static void Show(Form owner, Action manual, Func<Control, string> describe = null)
        {
            if (owner == null || owner.Text.StartsWith("Help: ", StringComparison.Ordinal)) return;
            var focused = FocusedControl(owner);
            var title = "Help: " + ControlName(focused);
            using (var dialog = new ApiKeyTestResultForm((describe ?? Description)(focused), title, manual))
                dialog.ShowDialog(owner);
            if (!focused.IsDisposed && focused.CanFocus) focused.Focus();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Application.RemoveMessageFilter(this);
        }
    }
}
