using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings = AppSettings.Load();
        private readonly Playback playback = new Playback();
        private SpeechProject project = new SpeechProject();
        private readonly Dictionary<string, SpeechProject> drafts = new Dictionary<string, SpeechProject>();
        private readonly List<NamedItem> models = new List<NamedItem>();
        private readonly List<NamedItem> voices = new List<NamedItem>();
        private CancellationTokenSource operation;
        private CancellationTokenSource balanceOperation;
        private string projectPath;
        private bool binding;
        private bool closeAfterCancel;
        private bool dubbingOpen;
        private static readonly SpeechMode[] Modes = { SpeechMode.TextToSpeech, SpeechMode.Dialogue, SpeechMode.Transcription, SpeechMode.VoiceChanger, SpeechMode.VoiceDesign, SpeechMode.VoiceIsolation, SpeechMode.ForcedAlignment, SpeechMode.VoiceRemix };
        private readonly ComboBox mode = Ui.Combo("Mode", new object[] { "Text to speech", "Dialogue", "Transcription", "Voice changer", "Voice design", "Voice isolation", "Transcript alignment", "Voice remix" });
        private readonly ComboBox voice = Ui.Combo("Voice", new object[0]);
        private readonly ComboBox voiceGroup = Ui.Combo("Voice group", VoiceGroups.Names.Cast<object>().ToArray());
        private readonly ComboBox model = Ui.Combo("Model", new object[0]);
        private readonly ComboBox format = Ui.Combo("Output format", SpeechProject.OutputFormats.Cast<object>().ToArray());
        private readonly NumericUpDown variations = Ui.Number("Variations", 1, 10, 1, 0);
        private readonly Label fixedFormat = new Label { Text = "Output: service-supplied audio", AutoSize = true };
        private readonly Label formatLabel = new Label { Text = "&Output format:", AutoSize = true };
        private readonly Label variationsLabel = new Label { Text = "Va&riations:", AutoSize = true };
        private readonly ShortcutTextBox prompt = new ShortcutTextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = false, ScrollBars = ScrollBars.Vertical, AccessibleName = "Speech text", ShortcutText = "Alt+M", Dock = DockStyle.Fill };
        private readonly AccessibleStatusTextBox status = new AccessibleStatusTextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Status log", ShortcutText = "Alt+S", Dock = DockStyle.Fill };
        private readonly ShortcutTextBox balance = new ShortcutTextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Credit balance", ShortcutText = "Alt+B", Dock = DockStyle.Fill, Text = "Enter an API key in Preferences to check your balance." };
        private readonly TextBox filename = Ui.Text("Base filename", false);
        private readonly TextBox input = Ui.Text("Input audio file", false);
        private readonly TextBox language = Ui.Text("Language code, blank for automatic", false);
        private readonly CheckBox diarize = new CheckBox { Text = "Identify spea&kers", AutoSize = true };
        private readonly CheckBox events = new CheckBox { Text = "Include audio &events", Checked = true, AutoSize = true };
        private readonly CheckBox removeNoise = new CheckBox { Text = "Remove background noise", AutoSize = true };
        private readonly FlowLayoutPanel audioOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel speechOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel transcriptionOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        private readonly ListBox outputs = new ListBox { AccessibleName = "Generated files and voice previews", Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly AccessibleStatusLabel progress = new AccessibleStatusLabel { Text = "Ready", AccessibleName = "Ready", AutoSize = true };
        private readonly Button generate;
        private readonly Button cancel;
        private readonly Label count = new Label { AutoSize = true };
        private readonly TableLayoutPanel selectors = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        private readonly Button voiceSettings;
        private readonly ShortcutButton previewVoice;
        private readonly List<string> previewFiles = new List<string>();
        private ToolStripMenuItem editDialogueMenu;
        private ToolStripMenuItem insertTagMenu;
        private readonly System.Windows.Forms.Timer draftTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        private string savedDraftJson;
        private readonly Button addDesign;

        public MainForm(string initialFile)
        {
            Text = Program.AppName; AccessibleName = Text; Size = new Size(1040, 780); MinimumSize = new Size(800, 600); StartPosition = FormStartPosition.CenterScreen;
            project.OutputFormat = settings.DefaultOutputFormat;
            playback.Failed += ex => { if (!IsDisposed) Ui.Result(this, "Could not play", ex.Message); };
            playback.Started += path => { if (!IsDisposed) Log("Playing " + Path.GetFileName(path) + "."); };
            Disposed += delegate { playback.Dispose(); };
            var menu = BuildMenu(); MainMenuStrip = menu;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, 700), ColumnCount = 1, RowCount = 11, Padding = new Padding(12) };
            prompt.MinimumSize = new Size(0, 100);
            for (int r = 0; r < 11; r++) root.RowStyles.Add(new RowStyle(r == 3 ? SizeType.Percent : SizeType.AutoSize, r == 3 ? 100 : 0));
            var modeRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top }; modeRow.Controls.Add(new Label { Text = "Mo&de:", AutoSize = true }); modeRow.Controls.Add(mode); root.Controls.Add(modeRow, 0, 0);
            balance.Height = 75; status.Height = 95; root.Controls.Add(balance, 0, 1); root.Controls.Add(status, 0, 2);
            var promptArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; promptArea.RowStyles.Add(new RowStyle(SizeType.AutoSize)); promptArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); promptArea.Controls.Add(count, 0, 0); promptArea.Controls.Add(prompt, 0, 1); root.Controls.Add(promptArea, 0, 3);
            var voiceRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            voiceGroup.Width = 150; voiceGroup.SelectedItem = settings.VoiceGroup;
            voiceRow.Controls.Add(voiceGroup); voiceRow.Controls.Add(new Label { Text = "&Voice:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }); voiceRow.Controls.Add(voice);
            previewVoice = new ShortcutButton { Text = "Preview voice", ShortcutText = "Ctrl+Shift+Space", AccessibleName = "Preview voice", AutoSize = true, Enabled = false };
            previewVoice.Click += async delegate { await PreviewSelectedVoice(); }; voiceRow.Controls.Add(previewVoice);
            selectors.Controls.Add(new Label { Text = "Voice vie&w:", AutoSize = true }, 0, 0); selectors.Controls.Add(voiceRow, 1, 0); selectors.SetColumnSpan(voiceRow, 3);
            selectors.Controls.Add(new Label { Text = "Mode&l:", AutoSize = true }, 0, 1); selectors.Controls.Add(model, 1, 1);
            voiceSettings = Ui.Button("Vo&ice settings...", VoiceSettings); selectors.Controls.Add(voiceSettings, 2, 1); selectors.Controls.Add(Ui.Button("Refresh voices", () => StartCatalogLoad()), 3, 1); root.Controls.Add(selectors, 0, 4);
            var fileRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 }; fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fileRow.Controls.Add(new Label { Text = "Base file&name:", AutoSize = true }, 0, 0); filename.Dock = DockStyle.Fill; fileRow.Controls.Add(filename, 1, 0); root.Controls.Add(fileRow, 0, 5);
            input.ReadOnly = true; input.Width = 450; audioOptions.Controls.Add(input); audioOptions.Controls.Add(Ui.Button("&Choose audio...", ChooseInput)); root.Controls.Add(audioOptions, 0, 6);
            speechOptions.Controls.Add(Ui.Button("Edit dialogue...", EditDialogue)); speechOptions.Controls.Add(new Label { Text = "Lang&uage:", AutoSize = true }); speechOptions.Controls.Add(language); language.Width = 80;
            transcriptionOptions.Controls.Add(diarize); transcriptionOptions.Controls.Add(events); transcriptionOptions.Controls.Add(removeNoise);
            var extra = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; extra.Controls.Add(speechOptions); extra.Controls.Add(transcriptionOptions); root.Controls.Add(extra, 0, 7);
            var outputRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; outputRow.Controls.Add(formatLabel); outputRow.Controls.Add(format); outputRow.Controls.Add(variationsLabel); outputRow.Controls.Add(variations); outputRow.Controls.Add(fixedFormat); root.Controls.Add(outputRow, 0, 8);
            var commands = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; generate = new ShortcutButton { Text = "&Generate", ShortcutText = "Ctrl+Enter", AutoSize = true }; generate.Click += async delegate { await Generate(); };
            cancel = Ui.Button("Cancel generation", () => { if (operation != null) operation.Cancel(); }); cancel.Enabled = false;
            addDesign = Ui.Button("Add preview voice...", AddDesign);
            var stop = new ShortcutButton { Text = "Stop", ShortcutText = "Esc", AutoSize = true }; stop.Click += delegate { StopPlayback(); };
            commands.Controls.Add(generate); commands.Controls.Add(cancel); commands.Controls.Add(Ui.Button("&Play", PlaySelected)); commands.Controls.Add(stop); commands.Controls.Add(Ui.Button("Save a cop&y...", SaveSelected)); commands.Controls.Add(addDesign); commands.Controls.Add(progress); root.Controls.Add(commands, 0, 9);
            outputs.Height = 80; root.Controls.Add(outputs, 0, 10); Controls.Add(root); Controls.Add(menu);
            mode.SelectedIndexChanged += delegate { if (!binding && mode.SelectedIndex >= 0) { CaptureProject(); drafts[project.Mode.ToString()] = project; var selected = Modes[mode.SelectedIndex]; project = drafts.ContainsKey(selected.ToString()) ? drafts[selected.ToString()] : FreshProject(selected); Bind(); } };
            model.SelectedIndexChanged += delegate { if (!binding) { project.ModelId = SelectedId(model); ApplyLimits(); insertTagMenu.Enabled = CanInsertSpeechTag(); } };
            voiceGroup.SelectedIndexChanged += delegate { if (binding) return; CaptureProject(); settings.VoiceGroup = Convert.ToString(voiceGroup.SelectedItem); FillVoices(); try { settings.Save(); } catch (Exception ex) { Ui.Result(this, "Could not save voice group", ex.Message); } };
            voice.SelectedIndexChanged += delegate { UpdateVoicePreview(); };
            prompt.TextChanged += delegate { UpdateCount(); }; outputs.DoubleClick += delegate { PlaySelected(); };
            FormClosing += OnFormClosingRequested;
            draftTimer.Tick += delegate { try { SaveDrafts(); } catch (Exception ex) { AppLog.WriteException("Save drafts", ex); draftTimer.Stop(); } };
            UpdateService.CanInstall = () => !IsDisposed && operation == null && !dubbingOpen;
            Shown += delegate { LoadDrafts(); if (!string.IsNullOrEmpty(initialFile)) OpenFile(initialFile); Bind(); prompt.Focus(); draftTimer.Start(); StartCatalogLoad(); RefreshBalance(); UpdateService.CheckAutomatically(this, settings); };
            ContextHelp.Attach(this, () => OpenLink(AppPaths.ManualPath), HelpDescription);
        }
        private ToolStripMenuItem Item(string text, Keys key, Action action)
        {
            var item = new ToolStripMenuItem(text, null, delegate { action(); });
            if (key != Keys.None)
            {
                item.ShortcutKeys = key;
                item.ShortcutKeyDisplayString = key == (Keys.Control | Keys.Oemcomma) ? "Ctrl+," : new KeysConverter().ConvertToString(key);
                item.AccessibleDescription = item.ShortcutKeyDisplayString;
            }
            return item;
        }
        private MenuStrip BuildMenu()
        {
            var m = new MenuStrip(); var file = new ToolStripMenuItem("&File"); var actions = new ToolStripMenuItem("&Actions"); var tools = new ToolStripMenuItem("&Tools"); var help = new ToolStripMenuItem("&Help");
            file.DropDownItems.Add(Item("&New project", Keys.Control | Keys.N, NewProject)); file.DropDownItems.Add(Item("&Open prompt or project...", Keys.Control | Keys.O, OpenProject)); file.DropDownItems.Add(Item("&Save project", Keys.Control | Keys.S, () => SaveProject(false))); file.DropDownItems.Add(Item("Save project &as...", Keys.Control | Keys.Shift | Keys.S, () => SaveProject(true)));
            file.DropDownItems.Add(Item("Open output fo&lder", Keys.Control | Keys.Shift | Keys.O, () => { Directory.CreateDirectory(settings.DefaultOutputFolder); OpenLink(settings.DefaultOutputFolder); })); file.DropDownItems.Add(Item("&Preferences...", Keys.Control | Keys.Oemcomma, Preferences)); file.DropDownItems.Add(Item("E&xit", Keys.Alt | Keys.F4, Close));
            editDialogueMenu = Item("Edit dialogue...", Keys.Control | Keys.P, EditDialogue); editDialogueMenu.Enabled = project.Mode == SpeechMode.Dialogue;
            actions.DropDownItems.Add(Item("&Generate", Keys.Control | Keys.Enter, async () => await Generate())); actions.DropDownItems.Add(editDialogueMenu); actions.DropDownItems.Add(Item("&Copy text", Keys.None, () => { if (prompt.TextLength > 0) Clipboard.SetText(prompt.Text); })); actions.DropDownItems.Add(Item("Save &text...", Keys.None, SaveText)); actions.DropDownItems.Add(Item("Use transcript as &speech", Keys.None, () => { var text = prompt.Text; mode.SelectedIndex = 0; prompt.Text = text; })); actions.DropDownItems.Add(Item("Refresh &balance", Keys.F5, RefreshBalance));
            insertTagMenu = Item("&Insert speech tag...", Keys.Control | Keys.Shift | Keys.T, InsertSpeechTag); actions.DropDownItems.Add(insertTagMenu);
            var play = Item("&Play selected", Keys.None, PlaySelected); play.ShortcutKeyDisplayString = play.AccessibleDescription = "Alt+P"; actions.DropDownItems.Add(play);
            var stop = Item("Stop pla&yback", Keys.None, StopPlayback); stop.ShortcutKeyDisplayString = stop.AccessibleDescription = "Esc"; actions.DropDownItems.Add(stop);
            tools.DropDownItems.Add(Item("&Voice library...", Keys.Control | Keys.L, () => Catalog("Voices"))); tools.DropDownItems.Add(Item("&Clone voice...", Keys.Control | Keys.Shift | Keys.N, CloneVoice)); tools.DropDownItems.Add(Item("Generation &history...", Keys.Control | Keys.H, () => Catalog("History"))); tools.DropDownItems.Add(Item("Pronunciation &dictionaries...", Keys.Control | Keys.D, () => Catalog("Dictionaries"))); tools.DropDownItems.Add(Item("&Audio settings...", Keys.Control | Keys.U, AudioDevices)); tools.DropDownItems.Add(Item("Open lo&g folder", Keys.None, () => OpenLink(AppPaths.LogFolder)));
            tools.DropDownItems.Add(Item("Automatic du&bbing...", Keys.Control | Keys.Shift | Keys.D, AutomaticDubbing));
            help.DropDownItems.Add(Item("Help for focused &control", Keys.F1, () => ContextHelp.Show(this, () => OpenLink(AppPaths.ManualPath), HelpDescription))); help.DropDownItems.Add(Item("&Manual", Keys.None, () => OpenLink(AppPaths.ManualPath))); help.DropDownItems.Add(Item("Check for &updates", Keys.Shift | Keys.F1, () => { if (operation == null) UpdateService.CheckForUpdates(this, settings, false); })); help.DropDownItems.Add(Item("&Project page", Keys.Control | Keys.F1, () => OpenLink(UpdateService.ProjectUrl))); help.DropDownItems.Add(Item("Usage anal&ytics", Keys.Alt | Keys.F1, () => OpenLink("https://elevenlabs.io/app/developers/analytics/usage"))); help.DropDownItems.Add(Item("&Software catalogue", Keys.None, () => OpenLink("https://onj.me/software"))); help.DropDownItems.Add(Item("&Donate", Keys.None, () => OpenLink("https://onj.me/donate"))); help.DropDownItems.Add(Item("&About", Keys.None, () => Ui.Result(this, "About", Program.AppName + " " + Program.Version + "\r\nBy Andre Louis\r\nUses ElevenLabs services and NAudio.\r\nNot affiliated with ElevenLabs.")));
            m.Items.AddRange(new ToolStripItem[] { file, actions, tools, help }); return m;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.Space)) { previewVoice.PerformClick(); return true; }
            if (keyData == Keys.Escape && (MainMenuStrip.ContainsFocus || MainMenuStrip.Items.OfType<ToolStripMenuItem>().Any(x => x.DropDown.Visible)))
                return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape) { StopPlayback(); if (operation != null) operation.Cancel(); return true; }
            if (keyData == Keys.Enter && outputs.Focused) { PlaySelected(); return true; }
            if (keyData == (Keys.Alt | Keys.M)) { prompt.Focus(); return true; }
            if (keyData == (Keys.Alt | Keys.S)) { status.Focus(); return true; }
            if (keyData == (Keys.Alt | Keys.B)) { balance.Focus(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { EditDialogue(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void AutomaticDubbing()
        {
            if (operation != null || dubbingOpen) return;
            dubbingOpen = true;
            try
            {
                using (var dialog = new DubbingForm(Client(), settings.DefaultOutputFolder, path => { AddOutput(path, null); if (settings.AutoPlayGenerations) playback.PlaySequence(new[] { path }, settings.PlaybackDevice); }, settings.CompletionSound && !settings.AutoPlayGenerations)) dialog.ShowDialog(this);
                RefreshBalance();
            }
            catch (Exception ex) { Ui.Result(this, "Could not open automatic dubbing", ex.Message); }
            finally { dubbingOpen = false; }
        }
        private void CaptureProject()
        {
            if (project.Mode != SpeechMode.Dialogue) project.Text = prompt.Text; project.Filename = filename.Text; project.InputFile = input.Text;
            if (voice.SelectedItem != null) project.VoiceId = SelectedId(voice); if (model.SelectedItem != null) project.ModelId = SelectedId(model);
            project.OutputFormat = Convert.ToString(format.SelectedItem); project.Variations = (int)variations.Value; project.Language = language.Text; project.Diarize = diarize.Checked; project.AudioEvents = events.Checked; project.RemoveNoise = removeNoise.Checked;
        }
        private void Bind()
        {
            binding = true;
            try
            {
                mode.SelectedIndex = Array.IndexOf(Modes, project.Mode); prompt.Text = SpeechProject.WindowsLines(project.Mode == SpeechMode.Dialogue ? string.Join("\n", project.Dialogue.Select(x => x.ToString()).ToArray()) : project.Text); prompt.ReadOnly = project.Mode == SpeechMode.Dialogue; filename.Text = project.Filename; input.Text = project.InputFile; language.Text = project.Language;
            format.SelectedItem = project.OutputFormat; variations.Value = Math.Max(1, Math.Min(10, project.Variations)); diarize.Checked = project.Diarize; events.Checked = project.AudioEvents; removeNoise.Checked = project.RemoveNoise;
                bool audio = project.Mode == SpeechMode.Transcription || project.Mode == SpeechMode.VoiceChanger || project.Mode == SpeechMode.VoiceIsolation || project.Mode == SpeechMode.ForcedAlignment; audioOptions.Visible = audio;
                speechOptions.Visible = project.Mode == SpeechMode.TextToSpeech || project.Mode == SpeechMode.Dialogue || project.Mode == SpeechMode.VoiceChanger || project.Mode == SpeechMode.Transcription;
                speechOptions.Controls[0].Visible = project.Mode == SpeechMode.Dialogue; voiceSettings.Visible = project.Mode == SpeechMode.TextToSpeech || project.Mode == SpeechMode.Dialogue || project.Mode == SpeechMode.VoiceChanger;
                transcriptionOptions.Visible = audio; diarize.Visible = events.Visible = project.Mode == SpeechMode.Transcription; removeNoise.Visible = project.Mode == SpeechMode.VoiceChanger;
                speechOptions.Controls[1].Visible = language.Visible = project.Mode != SpeechMode.VoiceChanger;
                addDesign.Visible = project.Mode == SpeechMode.VoiceDesign || project.Mode == SpeechMode.VoiceRemix;
                voice.Enabled = project.Mode == SpeechMode.TextToSpeech || project.Mode == SpeechMode.VoiceChanger || project.Mode == SpeechMode.VoiceRemix; variations.Enabled = project.Mode == SpeechMode.TextToSpeech || project.Mode == SpeechMode.Dialogue || project.Mode == SpeechMode.VoiceChanger; format.Enabled = variations.Enabled;
                format.Visible = formatLabel.Visible = variations.Visible = variationsLabel.Visible = variations.Enabled; fixedFormat.Visible = project.Mode == SpeechMode.VoiceIsolation;
                prompt.AccessibleName = project.Mode == SpeechMode.VoiceRemix ? "Voice changes" : project.Mode == SpeechMode.ForcedAlignment ? "Matching transcript" : project.Mode == SpeechMode.VoiceDesign ? "Voice description" : project.Mode == SpeechMode.Dialogue ? "Dialogue summary" : project.Mode == SpeechMode.Transcription ? "Transcript" : "Speech text";
                prompt.Visible = project.Mode != SpeechMode.VoiceChanger && project.Mode != SpeechMode.VoiceIsolation;
                editDialogueMenu.Enabled = operation == null && project.Mode == SpeechMode.Dialogue;
                insertTagMenu.Enabled = CanInsertSpeechTag();
                FillModels(); FillVoices(); ApplyLimits();
            }
            finally { binding = false; }
        }
        private void FillModels()
        {
            var list = models.Where(x => project.Mode == SpeechMode.VoiceChanger ? JsonData.Bool(x.Data, "can_do_voice_conversion") : JsonData.Bool(x.Data, "can_do_text_to_speech")).ToList();
            if (project.Mode == SpeechMode.Transcription) list = new List<NamedItem> { new NamedItem { Id = "scribe_v2", Name = "Scribe v2" }, new NamedItem { Id = "scribe_v1", Name = "Scribe v1" } };
            else if (project.Mode == SpeechMode.VoiceDesign) list = new List<NamedItem> { new NamedItem { Id = "eleven_ttv_v3", Name = "Voice Design v3" }, new NamedItem { Id = "eleven_multilingual_ttv_v2", Name = "Voice Design v2" } };
            else if (project.Mode == SpeechMode.VoiceIsolation) list = new List<NamedItem> { new NamedItem { Id = "audio_isolation", Name = "Voice Isolation" } };
            else if (project.Mode == SpeechMode.ForcedAlignment) list = new List<NamedItem> { new NamedItem { Id = "forced_alignment", Name = "Transcript Alignment" } };
            else if (project.Mode == SpeechMode.VoiceRemix) list = new List<NamedItem> { new NamedItem { Id = "eleven_ttv_v3", Name = "Voice Remix" } };
            else if (project.Mode == SpeechMode.Dialogue) list = list.Where(x => x.Id.StartsWith("eleven_v3") || x.Id.StartsWith("eleven_v4")).ToList();
            if (list.Count == 0) list.Add(new NamedItem { Id = project.Mode == SpeechMode.VoiceChanger ? "eleven_multilingual_sts_v2" : "eleven_v4", Name = project.Mode == SpeechMode.VoiceChanger ? "Multilingual Voice Changer v2" : "Eleven v4" });
            model.Items.Clear(); model.Items.AddRange(list.Cast<object>().ToArray()); Select(model, project.ModelId); project.ModelId = SelectedId(model);
        }
        private int TextLimit()
        {
            if (project.Mode == SpeechMode.VoiceDesign || project.Mode == SpeechMode.VoiceRemix) return 1000;
            if (project.Mode == SpeechMode.ForcedAlignment) return 675000;
            return LongSpeech.ModelLimit(model.SelectedItem as NamedItem, project.ModelId);
        }
        private int EditorLimit() { return project.Mode == SpeechMode.TextToSpeech && settings.AllowLongSpeech ? LongSpeech.MaximumCharacters : TextLimit(); }
        private void ApplyLimits() { prompt.MaxLength = project.Mode == SpeechMode.Transcription ? 0 : EditorLimit(); UpdateCount(); }
        private void UpdateCount() { var n = project.Mode == SpeechMode.Dialogue ? project.Dialogue.Sum(x => SpeechProject.TextLength(x.Text)) : SpeechProject.TextLength(prompt.Text); count.Text = project.Mode == SpeechMode.Transcription ? n + " characters" : n + " / " + (project.Mode == SpeechMode.Dialogue ? 2000 : EditorLimit()) + " characters"; count.AccessibleName = count.Text; Text = AccessibleName = Program.AppName + " - " + count.Text; }
        private string HelpDescription(Control control) { return control == prompt ? ContextHelp.Description(control) + "\r\n\r\n" + count.Text + "." : ContextHelp.Description(control); }
        private bool CanInsertSpeechTag() { return operation == null && project.Mode == SpeechMode.TextToSpeech && (project.ModelId.StartsWith("eleven_v3") || project.ModelId.StartsWith("eleven_v4")); }
        private void FillVoices()
        {
            var choices = voices.Where(x => VoiceGroups.Matches(x, settings.VoiceGroup)).ToList();
            var current = voices.FirstOrDefault(x => x.Id == project.VoiceId);
            if (current != null && choices.All(x => x.Id != current.Id)) choices.Add(new NamedItem { Id = current.Id, Name = current.Name + " (current, outside group)", Data = current.Data });
            voice.Items.Clear(); voice.Items.AddRange(choices.Cast<object>().ToArray()); Select(voice, project.VoiceId);
            UpdateVoicePreview();
        }
        private void UpdateVoicePreview() { previewVoice.Enabled = operation == null && voice.Enabled && VoicePreview.Url(voice.SelectedItem as NamedItem) != null; }
        private async Task PreviewSelectedVoice()
        {
            if (!previewVoice.Enabled) return;
            var uri = VoicePreview.Url(voice.SelectedItem as NamedItem); if (uri == null) return;
            StopPlayback();
            await Run("Downloading voice preview", async token =>
            {
                Directory.CreateDirectory(AppPaths.UserFolder);
                var path = Path.Combine(AppPaths.UserFolder, "Preview-" + Guid.NewGuid().ToString("N") + ".mp3"); previewFiles.Add(path);
                await Task.Run(() => VoicePreview.Download(uri, path, token)); token.ThrowIfCancellationRequested(); playback.Play(path, settings.PlaybackDevice);
                Log("Playing the existing voice preview. No generation credits were used.");
            });
        }
        private void InsertSpeechTag() { if (CanInsertSpeechTag()) SpeechTags.Show(this, prompt, EditorLimit()); }
        private static string SelectedId(ComboBox c) { var x = c.SelectedItem as NamedItem; return x == null ? "" : x.Id; }
        private static void Select(ComboBox c, string id) { var item = c.Items.Cast<NamedItem>().FirstOrDefault(x => x.Id == id); if (item == null && !string.IsNullOrEmpty(id)) { item = new NamedItem { Id = id, Name = "Unavailable: " + id }; c.Items.Add(item); } if (item != null) c.SelectedItem = item; else if (c.Items.Count > 0) c.SelectedIndex = 0; }
        private SpeechClient Client() { return new SpeechClient(AppPaths.LoadApiKey()); }
        private async void StartCatalogLoad()
        {
            if (operation != null || string.IsNullOrEmpty(AppPaths.LoadApiKey())) return;
            await Run("Refreshing voices and models", async token =>
            {
                var client = Client(); var modelJson = await Task.Run(() => client.Get("/v1/models", token));
                var a = JsonData.Serializer().DeserializeObject(modelJson) as object[]; models.Clear(); if (a != null) foreach (var d in a.OfType<Dictionary<string, object>>()) models.Add(new NamedItem { Id = JsonData.String(d, "model_id"), Name = JsonData.String(d, "name"), Data = d });
                var collected = new List<NamedItem>(); string cursor = ""; var seen = new HashSet<string>();
                do
                {
                    var data = JsonData.Object(await Task.Run(() => client.Get("/v2/voices?page_size=100" + (cursor.Length > 0 ? "&next_page_token=" + Uri.EscapeDataString(cursor) : ""), token)));
                    foreach (var d in JsonData.Items(data, "voices")) collected.Add(new NamedItem { Id = JsonData.String(d, "voice_id"), Name = JsonData.String(d, "name"), Data = d });
                cursor = JsonData.Bool(data, "has_more") ? JsonData.String(data, "next_page_token") : ""; if (cursor.Length > 0 && !seen.Add(cursor)) throw new InvalidDataException("The voice service repeated a page token.");
                } while (cursor.Length > 0 && collected.Count < 10000);
                CaptureProject(); voices.Clear(); voices.AddRange(collected.GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.Name)); Bind(); Log("Loaded " + voices.Count + " voices and " + models.Count + " models.");
            });
        }
        private async Task Run(string title, Func<CancellationToken, Task> work)
        {
            if (operation != null) return;
            var focusBefore = ContainsFocus ? ContextHelp.FocusedControl(this) : null;
            operation = new CancellationTokenSource(); SetBusy(true); Log(title + ".");
            // Disabling a focused selector moves focus forward. Restore only that automatic move.
            var displacedFocus = focusBefore != null && !focusBefore.Enabled ? ContextHelp.FocusedControl(this) : null;
            if (displacedFocus == focusBefore) displacedFocus = null;
            bool focusChanged = false;
            EventHandler leftDisplacedControl = delegate { focusChanged = true; };
            if (displacedFocus != null) displacedFocus.Leave += leftDisplacedControl;
            try { await work(operation.Token); }
            catch (OperationCanceledException) { Log("Cancelled. Completed files were kept."); }
            catch (Exception ex) { Log(ex.Message, false); AppLog.WriteException(title, ex); if (!closeAfterCancel) Ui.Result(this, title + " failed", ex.Message); }
            finally
            {
                bool restoreFocus = displacedFocus != null && !focusChanged && !closeAfterCancel && ContainsFocus && displacedFocus.Focused;
                if (displacedFocus != null) displacedFocus.Leave -= leftDisplacedControl;
                operation.Dispose(); operation = null; SetBusy(false);
                if (restoreFocus && !focusBefore.IsDisposed && focusBefore.CanFocus) focusBefore.Focus();
                if (closeAfterCancel) Close();
            }
        }
        private void SetBusy(bool busy)
        {
            generate.Enabled = !busy; cancel.Enabled = busy; mode.Enabled = !busy; selectors.Enabled = !busy;
            editDialogueMenu.Enabled = !busy && project.Mode == SpeechMode.Dialogue;
            insertTagMenu.Enabled = CanInsertSpeechTag();
            UpdateVoicePreview();
            if (busy) { progress.Text = progress.AccessibleName = "Working"; progress.NotifyNameChanged(); }
            else if (progress.Text == "Working") { progress.Text = progress.AccessibleName = "Ready"; progress.NotifyNameChanged(); }
        }
        private async Task Generate()
        {
            if (operation != null) return; CaptureProject();
            int requestLimit = TextLimit(); bool longSpeech = project.Mode == SpeechMode.TextToSpeech && settings.AllowLongSpeech && project.Text.Length > requestLimit; int parts = 1;
            try { project.Validate(EditorLimit()); if (longSpeech) parts = LongSpeech.Split(project.Text, requestLimit).Count; } catch (Exception ex) { Ui.Result(this, "Cannot generate", ex.Message); return; }
            bool single = project.Mode != SpeechMode.TextToSpeech && project.Mode != SpeechMode.Dialogue && project.Mode != SpeechMode.VoiceChanger;
            var confirmation = "Generate " + (single ? 1 : project.Variations * parts) + " " + mode.Text.ToLowerInvariant() + " request(s)? This sends your text or audio to ElevenLabs and may spend credits.";
            if (longSpeech) confirmation += "\r\n\r\nEach variation will use " + parts + " parts in your chosen format and be saved as one WAV at " + LongSpeech.SampleRate(project.OutputFormat) + " Hz. Verified parts from an identical unfinished job will be reused. An interrupted request already accepted by ElevenLabs may still have incurred a charge; the app does not retry it automatically.";
            if (MessageBox.Show(this, confirmation, "Confirm generation", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var request = JsonData.Serializer().Deserialize<SpeechProject>(JsonData.Encode(project)); var selectedModel = model.SelectedItem as NamedItem;
            var selectedVoice = voice.SelectedItem as NamedItem;
            var folder = FileNames.GenerationFolder(settings.DefaultOutputFolder, request.Mode, selectedVoice == null ? request.VoiceId : selectedVoice.Name.Replace(" (current, outside group)", ""));
            StopPlayback(); var audio = new List<string>(); bool complete = false;
            await Run("Generating " + mode.Text.ToLowerInvariant(), async token =>
            {
                Directory.CreateDirectory(folder); var timer = Stopwatch.StartNew(); var client = Client(); int total = single ? 1 : request.Variations;
                List<ApiResult> longResults = null;
                if (longSpeech)
                {
                    var longStem = FileNames.Stem(string.IsNullOrWhiteSpace(request.Filename) ? string.Join(" ", request.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray()) : request.Filename);
                    var updates = new Progress<string>(message => { if (!IsDisposed) Log(message); });
                    longResults = await Task.Run(() => LongSpeech.Generate(request, selectedModel, folder, longStem, requestLimit, token, message => ((IProgress<string>)updates).Report(message), (part, context, output, cancellation) => client.Generate(part, selectedModel, output, cancellation, context)));
                }
                for (int i = 1; i <= total; i++)
                {
                    token.ThrowIfCancellationRequested(); var stem = FileNames.Stem(string.IsNullOrWhiteSpace(request.Filename) ? string.Join(" ", request.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray()) : request.Filename);
                    if ((request.Mode == SpeechMode.Transcription || request.Mode == SpeechMode.ForcedAlignment || request.Mode == SpeechMode.VoiceIsolation) && string.IsNullOrWhiteSpace(request.Filename)) stem = FileNames.Stem(Path.GetFileNameWithoutExtension(request.InputFile));
                    var path = FileNames.Next(folder, stem + (total > 1 ? "_v" + i : ""), request.Mode == SpeechMode.VoiceIsolation || request.OutputFormat.StartsWith("pcm_") ? ".wav" : ".mp3"); var one = Stopwatch.StartNew();
                    var result = longResults == null ? await Task.Run(() => client.Generate(request, selectedModel, path, token)) : longResults[i - 1];
                    if (request.Mode == SpeechMode.Transcription || request.Mode == SpeechMode.ForcedAlignment)
                    {
                        var data = JsonData.Object(result.Json); prompt.Text = SpeechProject.WindowsLines(request.Mode == SpeechMode.ForcedAlignment ? request.Text : JsonData.String(data, "text"));
                        var txt = FileNames.Next(folder, stem, ".txt"); FileNames.AtomicText(txt, prompt.Text); AddOutput(txt, null);
                        var srt = Transcript.Subtitles(data, request.Mode == SpeechMode.ForcedAlignment); if (srt.Length > 0) FileNames.AtomicText(Path.ChangeExtension(txt, ".srt"), srt);
                        if (settings.SaveDetails) SaveDetails(folder, Path.GetFileNameWithoutExtension(txt), data);
                    }
                    else if (request.Mode == SpeechMode.VoiceDesign || request.Mode == SpeechMode.VoiceRemix)
                    {
                        int previewNumber = 0;
                        foreach (var preview in JsonData.Items(JsonData.Object(result.Json), "previews"))
                        {
                            var previewPath = FileNames.Next(folder, stem + " - Preview " + ++previewNumber, SpeechClient.PreviewExtension(preview)); var bytes = Convert.FromBase64String(JsonData.String(preview, "audio_base_64"));
                            if (bytes.Length == 0) throw new InvalidDataException("Empty voice preview."); File.WriteAllBytes(previewPath, bytes); preview["voice_description"] = request.Text; AddOutput(previewPath, preview);
                            audio.Add(previewPath);
                        }
                        if (previewNumber == 0) throw new InvalidDataException("No voice previews were returned.");
                    }
                    else
                    {
                        AddOutput(result.File, null);
                        audio.Add(result.File);
                        if (settings.SaveDetails) SaveDetails(folder, Path.GetFileNameWithoutExtension(result.File), new Dictionary<string, object> { { "project", request }, { "request_id", result.RequestId }, { "generated_utc", DateTime.UtcNow.ToString("o") } });
                    }
                    if (!longSpeech) Log("Request " + i + " completed in " + one.Elapsed.TotalSeconds.ToString("F1") + " seconds.");
                }
                Log("Generation complete. Elapsed: " + timer.Elapsed.TotalSeconds.ToString("F1") + " seconds."); progress.Text = progress.AccessibleName = "Generation complete"; progress.NotifyNameChanged(); if (settings.CompletionSound && !(settings.AutoPlayGenerations && audio.Count > 0)) SystemSounds.Asterisk.Play();
                complete = true;
            });
            if (complete && !closeAfterCancel && !IsDisposed && settings.AutoPlayGenerations) playback.PlaySequence(audio, settings.PlaybackDevice);
            RefreshBalance(); SaveDrafts();
        }
        private static void SaveDetails(string folder, string stem, object data) { FileNames.AtomicText(Path.Combine(folder, "Details", stem + ".json"), ReadableJson.Format(JsonData.Encode(data))); }
        private void AddOutput(string path, Dictionary<string, object> data) { outputs.Items.Add(new NamedItem { Id = path, Name = Path.GetFileName(path), Data = data }); outputs.SelectedIndex = outputs.Items.Count - 1; if (outputs.Items.Count > 200) outputs.Items.RemoveAt(0); }
        private void StopPlayback() { playback.Stop(); foreach (var path in previewFiles) try { File.Delete(path); } catch (IOException) { } previewFiles.Clear(); }
        private void PlaySelected() { var item = outputs.SelectedItem as NamedItem; if (item == null) return; try { if (Path.GetExtension(item.Id) == ".txt") OpenLink(item.Id); else playback.Play(item.Id, settings.PlaybackDevice); } catch (Exception ex) { Ui.Result(this, "Could not play", ex.Message); } }
        private void SaveSelected() { var item = outputs.SelectedItem as NamedItem; if (item == null) return; using (var d = new SaveFileDialog { FileName = item.Name, Filter = "Audio or text|*" + Path.GetExtension(item.Id), InitialDirectory = settings.DefaultOutputFolder }) if (d.ShowDialog(this) == DialogResult.OK && !string.Equals(d.FileName, item.Id, StringComparison.OrdinalIgnoreCase)) try { File.Copy(item.Id, d.FileName, true); } catch (Exception ex) { Ui.Result(this, "Could not save", ex.Message); } }
        private async void AddDesign()
        {
            if (operation != null) return;
            var item = outputs.SelectedItem as NamedItem; if (item == null || item.Data == null || JsonData.String(item.Data, "generated_voice_id").Length == 0) { Ui.Result(this, "Designed voice", "Select a generated voice preview first."); return; }
            var name = ToolDialogs.Ask(this, "Save designed voice", "Voice &name:", filename.Text); if (string.IsNullOrWhiteSpace(name)) return;
            if (MessageBox.Show(this, "Save this designed voice to your ElevenLabs account?", "Save voice", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var description = JsonData.String(item.Data, "voice_description");
            await Run("Saving designed voice", async token => { await Task.Run(() => Client().Post("/v1/text-to-voice", new { voice_name = name, voice_description = description, generated_voice_id = JsonData.String(item.Data, "generated_voice_id") }, token)); Log("Designed voice saved."); }); StartCatalogLoad();
        }
        private void Log(string text, bool persist = true)
        {
            if (persist) AppLog.Write(text);
            string value = status.Text + SpeechProject.WindowsLines(text) + Environment.NewLine;
            int removed = value.Length > 50000 ? value.IndexOf('\n', value.Length - 35000) + 1 : 0;
            status.UpdateReadableText(value.Substring(removed), removed);
        }
        private async void RefreshBalance()
        {
            if (balanceOperation != null || string.IsNullOrEmpty(AppPaths.LoadApiKey())) return; balanceOperation = new CancellationTokenSource();
            try { var value = await Task.Run(() => SubscriptionBalance.Parse(Client().Get("/v1/user/subscription", balanceOperation.Token))); if (!IsDisposed) balance.UpdateReadableText(value.Format(DateTimeOffset.Now)); }
            catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) balance.UpdateReadableText("Could not check the credit balance: " + ex.Message); }
            finally { balanceOperation.Dispose(); balanceOperation = null; }
        }
        private void Preferences() { Preferences(0); }
        private void Preferences(int tab) { if (operation != null) return; var previousFormat = settings.DefaultOutputFormat; using (var f = new PreferencesForm(settings, tab)) if (f.ShowDialog(this) == DialogResult.OK) { StopPlayback(); if (settings.DefaultOutputFormat != previousFormat) { project.OutputFormat = settings.DefaultOutputFormat; format.SelectedItem = project.OutputFormat; } ApplyLimits(); StartCatalogLoad(); RefreshBalance(); } }
        private void ChooseInput() { using (var d = new OpenFileDialog { Filter = "Audio and video|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.mp4;*.webm|All files|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) input.Text = d.FileName; }
        private void VoiceSettings() { if (operation != null) return; CaptureProject(); ToolDialogs.VoiceSettings(this, project, model.SelectedItem as NamedItem); }
        private void EditDialogue() { if (operation != null || project.Mode != SpeechMode.Dialogue) return; CaptureProject(); ToolDialogs.Dialogue(this, project, voices); Bind(); }
        private void AudioDevices()
        {
            Preferences(3);
        }
        private void CloneVoice() { if (operation == null) { ToolDialogs.Clone(this, Client()); StartCatalogLoad(); } }
        private void Catalog(string kind)
        {
            if (operation != null) return;
            using (var f = new CatalogForm(kind, Client(), settings, playback, project)) { f.ShowDialog(this); if (f.SelectedVoice != null) { project.VoiceId = f.SelectedVoice.Id; voices.Add(f.SelectedVoice); voice.Items.Add(f.SelectedVoice); voice.SelectedItem = f.SelectedVoice; } }
        }
        private void NewProject() { if (operation != null) return; CaptureProject(); drafts[project.Mode.ToString()] = project; project = FreshProject(project.Mode); projectPath = null; Bind(); }
        private SpeechProject FreshProject(SpeechMode selectedMode) { var value = SpeechProject.ForMode(selectedMode); value.OutputFormat = settings.DefaultOutputFormat; return value; }
        private void OpenProject() { if (operation != null) return; using (var d = new OpenFileDialog { Filter = "Speech project or text|*.json;*.txt|All files|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName); }
        private void OpenFile(string path)
        {
            if (operation != null) return;
            try
            {
                if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The project is too large.");
                var text = File.ReadAllText(path); if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)) { prompt.Text = SpeechProject.WindowsLines(text); projectPath = null; return; }
                var loaded = SpeechProject.FromJson(text);
                loaded.ValidateStructure(); project = loaded; projectPath = path; Bind();
            }
            catch (Exception ex) { Ui.Result(this, "Could not open", ex.Message); }
        }
        private void SaveProject(bool saveAs)
        {
            CaptureProject(); if (saveAs || string.IsNullOrEmpty(projectPath)) using (var d = new SaveFileDialog { Filter = "Speech project|*.speech.json", FileName = FileNames.Stem(filename.Text) + ".speech.json", InitialDirectory = Path.Combine(AppPaths.UserFolder, "Projects") }) { if (d.ShowDialog(this) != DialogResult.OK) return; projectPath = d.FileName; }
            try { FileNames.AtomicText(projectPath, ReadableJson.Format(JsonData.Encode(project))); Log("Project saved."); } catch (Exception ex) { Ui.Result(this, "Could not save project", ex.Message); }
        }
        private void SaveText() { using (var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = FileNames.Stem(filename.Text) + ".txt", InitialDirectory = settings.DefaultOutputFolder }) if (d.ShowDialog(this) == DialogResult.OK) try { FileNames.AtomicText(d.FileName, prompt.Text); } catch (Exception ex) { Ui.Result(this, "Could not save text", ex.Message); } }
        private void LoadDrafts()
        {
            try { if (!File.Exists(AppPaths.DraftPath)) return; if (new FileInfo(AppPaths.DraftPath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Drafts are too large."); var loaded = JsonData.Serializer().Deserialize<Dictionary<string, SpeechProject>>(File.ReadAllText(AppPaths.DraftPath)); if (loaded == null || loaded.Any(x => x.Value == null)) throw new InvalidDataException("Invalid drafts."); foreach (var pair in loaded) pair.Value.ValidateStructure(); foreach (var pair in loaded) drafts[pair.Key] = pair.Value; if (drafts.ContainsKey("Last")) project = drafts["Last"]; }
            catch (Exception ex) { Log("Could not restore drafts: " + ex.Message); }
        }
        private void SaveDrafts() { CaptureProject(); drafts[project.Mode.ToString()] = project; drafts["Last"] = project; string json = JsonData.Encode(drafts); if (json == savedDraftJson) return; FileNames.AtomicText(AppPaths.DraftPath, ReadableJson.Format(json)); savedDraftJson = json; }
        private void OnFormClosingRequested(object sender, FormClosingEventArgs e)
        {
            if (operation != null) { e.Cancel = true; if (MessageBox.Show(this, "Cancel the active request and close? Completed files will be kept. A request already accepted by ElevenLabs may still incur charges.", "Close", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) { closeAfterCancel = true; operation.Cancel(); } return; }
            draftTimer.Stop(); draftTimer.Dispose(); if (balanceOperation != null) balanceOperation.Cancel(); try { SaveDrafts(); settings.Save(); } catch (Exception ex) { AppLog.WriteException("Save state", ex); } StopPlayback(); playback.Dispose();
        }
        private void OpenLink(string path) { try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch (Exception ex) { Ui.Result(this, "Could not open", ex.Message); } }
    }
}
