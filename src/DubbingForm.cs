using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class DubbingForm : Form
    {
        private DubbingJob job = new DubbingJob();
        private readonly SpeechClient client;
        private readonly string outputFolder;
        private readonly string jobsFolder = Path.Combine(AppPaths.UserFolder, "Dubbing jobs");
        private readonly Action<string> completed;
        private readonly bool completionSound;
        private CancellationTokenSource operation;
        private bool closeWhenStopped;
        private readonly ComboBox sourceType = Ui.Combo("Dubbing source", new object[] { "Local audio or video file", "Public HTTPS URL" });
        private readonly TextBox source = Ui.Text("Source audio or video", false);
        private readonly TextBox name = Ui.Text("Dubbing job name", false);
        private readonly ComboBox from = Ui.Combo("Source language", DubbingLanguages.Choices.Cast<object>().ToArray());
        private readonly ComboBox to = Ui.Combo("Target language", DubbingLanguages.Choices.Cast<object>().ToArray());
        private readonly AccessibleStatusTextBox status = new AccessibleStatusTextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Dubbing status", Dock = DockStyle.Fill, Text = "Ready." };
        private readonly Button start;
        private readonly Button cancel;
        private readonly Button browse;
        private readonly Button open;
        private readonly Button fresh;
        private readonly Button close;
        internal DubbingForm(SpeechClient client, string outputFolder, Action<string> completed, bool completionSound = false)
        {
            this.client = client; this.outputFolder = outputFolder; this.completed = completed; this.completionSound = completionSound;
            Text = AccessibleName = "Automatic dubbing"; Size = new Size(780, 520); MinimumSize = new Size(600, 450); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            var root = Ui.Layout(2);
            sourceType.AccessibleDescription = "Choose a local recording or a public HTTPS link. Sources are sent to ElevenLabs only after confirmation.";
            source.AccessibleDescription = "Audio or video to translate, up to 500 MB for a local file. The original file is not changed.";
            name.AccessibleDescription = "Optional job and output filename. Leave blank to use the source name.";
            from.DropDownStyle = to.DropDownStyle = ComboBoxStyle.DropDown;
            from.Items.Insert(0, new NamedItem { Id = "", Name = "Automatic detection" }); from.SelectedIndex = 0;
            from.AccessibleDescription = "Choose the source language, automatic detection, or type a language code.";
            to.AccessibleDescription = "Choose the translation language or type a supported language code, such as es or fr-CA.";
            Ui.Row(root, "Source &type:", sourceType);
            var fileRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true }; fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            source.Dock = DockStyle.Fill; fileRow.Controls.Add(source); browse = Ui.Button("&Browse...", Choose); fileRow.Controls.Add(browse); Ui.Row(root, "&Source:", fileRow);
            Ui.Row(root, "&Name:", name); Ui.Row(root, "&From language:", from); Ui.Row(root, "T&o language:", to);
            int r = root.RowCount++; root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.Controls.Add(status, 0, r); root.SetColumnSpan(status, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            start = Ui.Button("Start &dubbing", async () => await Start()); start.AccessibleDescription = "Ctrl+Enter. Create a paid dubbing project after confirmation, or resume the saved project without creating another.";
            cancel = Ui.Button("Stop &waiting", () => { if (operation != null) operation.Cancel(); }); cancel.AccessibleDescription = "Stop local monitoring. ElevenLabs may continue and charge for an accepted job. Resume retrieves the same job.";
            open = Ui.Button("Open &job...", OpenJob); fresh = Ui.Button("N&ew job", NewJob); close = Ui.Button("&Close", Close);
            buttons.Controls.AddRange(new Control[] { start, cancel, open, fresh, close }); r = root.RowCount++; root.Controls.Add(buttons, 0, r); root.SetColumnSpan(buttons, 2); Controls.Add(root); CancelButton = close;
            sourceType.SelectedIndexChanged += delegate { browse.Enabled = operation == null && job.ProjectId.Length == 0 && sourceType.SelectedIndex == 0; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (operation != null) { e.Cancel = true; closeWhenStopped = true; operation.Cancel(); } };
            var last = Path.Combine(jobsFolder, "Last.dubbing.json");
            if (File.Exists(last)) try { job = DubbingJob.Read(last); } catch (Exception ex) { status.Text = "Could not restore the previous job: " + ex.Message; }
            Bind();
        }
        private void Bind()
        {
            sourceType.SelectedIndex = job.UseUrl ? 1 : 0; source.Text = job.UseUrl ? job.SourceUrl : job.InputFile; name.Text = job.Name;
            SetLanguage(from, job.SourceLanguage); SetLanguage(to, job.TargetLanguage);
            if (job.ProjectId.Length > 0) status.UpdateReadableText("Saved project: " + job.ProjectId + (job.OutputFile.Length > 0 ? "\r\nSaved audio: " + job.OutputFile : "\r\nResume checks the existing job without creating another."));
            SetBusy(false);
        }
        private static void SetLanguage(ComboBox box, string code) { var item = box.Items.OfType<NamedItem>().FirstOrDefault(x => x.Id == code); if (item != null) box.SelectedItem = item; else box.Text = code; }
        private static string Language(ComboBox box) { var item = box.SelectedItem as NamedItem; return item != null && item.Name == box.Text ? item.Id : DubbingLanguages.Code(box.Text); }
        private void SetBusy(bool busy)
        {
            bool editable = !busy && job.ProjectId.Length == 0;
            sourceType.Enabled = source.Enabled = name.Enabled = from.Enabled = to.Enabled = editable; browse.Enabled = editable && sourceType.SelectedIndex == 0;
            start.Text = job.ProjectId.Length > 0 ? "&Resume job" : "Start &dubbing"; start.Enabled = open.Enabled = fresh.Enabled = !busy; cancel.Enabled = busy;
        }
        private void Choose() { using (var panel = new OpenFileDialog { Filter = "Audio and video|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.mp4;*.mov;*.mkv;*.webm|All files|*.*" }) if (panel.ShowDialog(this) == DialogResult.OK) { source.Text = panel.FileName; if (name.Text.Length == 0) name.Text = Path.GetFileNameWithoutExtension(panel.FileName); } }
        private void OpenJob() { Directory.CreateDirectory(jobsFolder); using (var panel = new OpenFileDialog { InitialDirectory = jobsFolder, Filter = "Dubbing jobs|*.dubbing.json" }) if (panel.ShowDialog(this) == DialogResult.OK) try { job = DubbingJob.Read(panel.FileName); Bind(); } catch (Exception ex) { Ui.Result(this, "Could not open job", ex.Message); } }
        private void NewJob() { job = new DubbingJob(); status.UpdateReadableText("Ready."); Bind(); source.Focus(); }
        private void Save() { job.Save(jobsFolder); FileNames.AtomicText(Path.Combine(jobsFolder, "Last.dubbing.json"), ReadableJson.Format(JsonData.Encode(job))); }
        private async Task Start()
        {
            if (operation != null) return;
            bool creating = job.ProjectId.Length == 0;
            if (creating) { job.UseUrl = sourceType.SelectedIndex == 1; job.InputFile = job.UseUrl ? "" : source.Text.Trim().Trim('"'); job.SourceUrl = job.UseUrl ? source.Text.Trim() : ""; job.Name = name.Text.Trim(); job.SourceLanguage = Language(from); job.TargetLanguage = Language(to); }
            try { job.Validate(creating); }
            catch (Exception ex) { Ui.Result(this, "Cannot start dubbing", ex.Message); return; }
            if (creating && MessageBox.Show(this, "Send this recording to ElevenLabs and translate it into " + job.TargetLanguage + "? You must have the necessary rights and speaker consent. Creating the project spends credits, even before output is ready. Stopping local monitoring does not cancel that charge.", "Confirm automatic dubbing", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            operation = new CancellationTokenSource(); SetBusy(true);
            var token = operation.Token; var service = new DubbingService(client);
            try
            {
                if (creating) { status.UpdateReadableText("Uploading source and creating dubbing project."); await Task.Run(() => service.Create(job, token)); Save(); }
                var folder = Path.Combine(outputFolder, "Dubbing", FileNames.Stem(job.TargetLanguage)); Directory.CreateDirectory(folder);
                var path = await service.Wait(job, folder, message => status.UpdateReadableText(status.Text + "\r\n" + message), Save, token);
                status.UpdateReadableText(status.Text + "\r\nDubbing complete. Saved " + path + "."); completed(path);
                if (completionSound) System.Media.SystemSounds.Asterisk.Play();
            }
            catch (OperationCanceledException) { status.UpdateReadableText(status.Text + "\r\nStopped waiting. " + (job.ProjectId.Length > 0 ? "Resume this saved job later; it may still be processing on ElevenLabs." : "The submission may have reached ElevenLabs. Check your dubbing projects before starting again.")); }
            catch (Exception ex) { status.UpdateReadableText(status.Text + "\r\n" + ex.Message); Ui.Result(this, "Dubbing could not finish", ex.Message + (job.ProjectId.Length == 0 ? "\r\nCheck your ElevenLabs projects before retrying a submission that failed after uploading." : "\r\nYour job is retained. Resume it instead of creating another.")); }
            finally { operation.Dispose(); operation = null; SetBusy(false); if (closeWhenStopped) Close(); }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { start.PerformClick(); return true; }
            return base.ProcessCmdKey(ref message, keys);
        }
    }
}
