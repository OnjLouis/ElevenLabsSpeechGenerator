using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal static class ToolDialogs
    {
        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var f = Ui.Dialog(title, new Size(600, 300)))
            {
                var p = Ui.Layout(2); var text = Ui.Text(label.Replace("&", "").TrimEnd(':'), false); text.Text = initial; Ui.Row(p, label, text); f.Controls.Add(p);
                var ok = Ui.Button("&OK", () => { f.DialogResult = DialogResult.OK; }); ok.Dock = DockStyle.Bottom; f.Controls.Add(ok); Ui.CloseButton(f); f.AcceptButton = ok; f.Shown += delegate { text.Focus(); };
                return f.ShowDialog(owner) == DialogResult.OK ? text.Text.Trim() : null;
            }
        }
        public static void VoiceSettings(IWin32Window owner, SpeechProject project, NamedItem model)
        {
            using (var f = Ui.Dialog("Voice settings", new Size(620, 470)))
            {
                var p = Ui.Layout(2); var stability = Ui.Number("Stability percent", 0, 100, (decimal)project.Stability * 100, 0); var similarity = Ui.Number("Similarity percent", 0, 100, (decimal)project.Similarity * 100, 0); var style = Ui.Number("Style percent", 0, 100, (decimal)project.Style * 100, 0); var speed = Ui.Number("Speed", .25m, 4m, (decimal)project.Speed, 2);
                var boost = new CheckBox { Text = "Speaker &boost", Checked = project.SpeakerBoost, AutoSize = true };
                style.Enabled = !project.ModelId.StartsWith("eleven_v4") && model != null && JsonData.Bool(model.Data, "can_use_style"); boost.Enabled = !project.ModelId.StartsWith("eleven_v4") && model != null && JsonData.Bool(model.Data, "can_use_speaker_boost"); speed.Enabled = !project.ModelId.StartsWith("eleven_v4");
                if (project.ModelId == "eleven_v3" || project.Mode == SpeechMode.Dialogue) { stability.Increment = 50; stability.Value = stability.Value < 25 ? 0 : stability.Value < 75 ? 50 : 100; }
                Ui.Row(p, "&Stability (percent):", stability); Ui.Row(p, "Si&milarity (percent):", similarity); Ui.Row(p, "S&tyle (percent):", style); Ui.Row(p, "Spee&d:", speed); Ui.Row(p, "", boost);
                f.Controls.Add(p);
                var ok = Ui.Button("&OK", () => { var candidate = (double)stability.Value / 100; if ((project.ModelId == "eleven_v3" || project.Mode == SpeechMode.Dialogue) && candidate != 0 && candidate != .5 && candidate != 1) { Ui.Result(f, "Stability", "Choose 0, 50, or 100 percent."); return; } project.Stability = candidate; project.Similarity = (double)similarity.Value / 100; project.Style = (double)style.Value / 100; project.Speed = (double)speed.Value; project.SpeakerBoost = boost.Checked; f.DialogResult = DialogResult.OK; }); ok.Dock = DockStyle.Bottom; f.Controls.Add(ok); Ui.CloseButton(f); f.AcceptButton = ok; f.ShowDialog(owner);
            }
        }
        public static void Dialogue(IWin32Window owner, SpeechProject project, List<NamedItem> voices)
        {
            using (var f = Ui.Dialog("Dialogue", new Size(850, 650)))
            {
                var lines = project.Dialogue.Select(x => new DialogueLine { VoiceId = x.VoiceId, VoiceName = x.VoiceName, Text = x.Text }).ToList();
                var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) }; p.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var list = new ListBox { AccessibleName = "Dialogue lines", Dock = DockStyle.Fill }; var voice = Ui.Combo("Voice for this line", voices.Cast<object>().ToArray()); var text = Ui.Text("Text for this line", true); text.Dock = DockStyle.Fill; text.MaxLength = 2000;
                int selected = -1; bool loading = false;
                Action save = () => { if (selected >= 0 && selected < lines.Count && !loading) { lines[selected].Text = text.Text; var v = voice.SelectedItem as NamedItem; if (v != null) { lines[selected].VoiceId = v.Id; lines[selected].VoiceName = v.Name; } } };
                Action load = () => { loading = true; text.Text = selected >= 0 ? SpeechProject.WindowsLines(lines[selected].Text) : ""; voice.SelectedItem = selected >= 0 ? voices.FirstOrDefault(x => x.Id == lines[selected].VoiceId) : null; text.Enabled = selected >= 0; loading = false; };
                Action refresh = () => { loading = true; list.Items.Clear(); list.Items.AddRange(lines.Cast<object>().ToArray()); if (selected >= 0 && selected < lines.Count) list.SelectedIndex = selected; loading = false; load(); };
                list.SelectedIndexChanged += delegate { if (loading) return; save(); selected = list.SelectedIndex; load(); };
                var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; row.Controls.Add(new Label { Text = "&Voice:", AutoSize = true }); row.Controls.Add(voice);
                row.Controls.Add(Ui.Button("&Add line", () => { save(); var v = voice.SelectedItem as NamedItem; if (v == null) return; lines.Add(new DialogueLine { VoiceId = v.Id, VoiceName = v.Name, Text = "" }); selected = lines.Count - 1; refresh(); text.Text = ""; text.Focus(); }));
                row.Controls.Add(Ui.Button("&Remove", () => { if (selected < 0) return; lines.RemoveAt(selected); selected = Math.Min(selected, lines.Count - 1); refresh(); }));
                Action<int> move = delta => { save(); int n = selected + delta; if (selected < 0 || n < 0 || n >= lines.Count) return; var line = lines[selected]; lines.RemoveAt(selected); lines.Insert(n, line); selected = n; refresh(); };
                row.Controls.Add(Ui.Button("Move &up", () => move(-1))); row.Controls.Add(Ui.Button("Move &down", () => move(1)));
                var tags = new ShortcutButton { Text = "Insert ta&g...", ShortcutText = "Ctrl+Shift+T", AutoSize = true };
                Action insertTag = () => { if (selected >= 0) SpeechTags.Show(f, text, 2000 - lines.Where((x, i) => i != selected).Sum(x => SpeechProject.TextLength(x.Text))); };
                tags.Click += delegate { insertTag(); }; row.Controls.Add(tags);
                f.KeyPreview = true; f.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyData == (Keys.Control | Keys.Shift | Keys.T)) { insertTag(); e.Handled = e.SuppressKeyPress = true; } };
                p.Controls.Add(list, 0, 0); p.Controls.Add(row, 0, 1); p.Controls.Add(text, 0, 2);
                var footer = new FlowLayoutPanel { AutoSize = true }; footer.Controls.Add(new Label { Text = "Up to 2,000 characters and 10 voices.", AutoSize = true });
                footer.Controls.Add(Ui.Button("&OK", () => { save(); if (lines.Sum(x => SpeechProject.TextLength(x.Text)) > 2000 || lines.Select(x => x.VoiceId).Distinct().Count() > 10) { Ui.Result(f, "Dialogue too long", "Use no more than 2,000 characters and 10 voices."); return; } project.Dialogue = lines; f.DialogResult = DialogResult.OK; }));
                p.Controls.Add(footer, 0, 3); f.Controls.Add(p); Ui.CloseButton(f); refresh(); if (lines.Count > 0) list.SelectedIndex = 0; else if (voice.Items.Count > 0) voice.SelectedIndex = 0; f.ShowDialog(owner);
            }
        }
        public static void Clone(IWin32Window owner, SpeechClient client)
        {
            using (var f = Ui.Dialog("Clone voice", new Size(680, 700)))
            {
                CancellationTokenSource cancellation = null;
                var p = Ui.Layout(2); var name = Ui.Text("Voice name", false); var description = Ui.Text("Voice description", true); var samples = new ListBox { AccessibleName = "Voice samples", Height = 130 }; var paths = new List<string>();
                Ui.Row(p, "&Name:", name); Ui.Row(p, "&Description:", description); Ui.Row(p, "Samples:", samples);
                Ui.Row(p, "", Ui.Button("&Add audio files...", () => { using (var d = new OpenFileDialog { Multiselect = true, Filter = "Audio|*.wav;*.mp3;*.m4a;*.flac|All files|*.*" }) if (d.ShowDialog(f) == DialogResult.OK) foreach (var path in d.FileNames) if (!paths.Contains(path)) { paths.Add(path); samples.Items.Add(Path.GetFileName(path)); } }));
                Ui.Row(p, "", Ui.Button("&Remove sample", () => { int i = samples.SelectedIndex; if (i >= 0) { paths.RemoveAt(i); samples.Items.RemoveAt(i); } }));
                var consent = new CheckBox { Text = "I &have the rights and consent to clone this voice", AutoSize = true }; Ui.Row(p, "", consent);
                var create = Ui.Button("&Create voice", () => { }); bool busy = false;
                var status = new AccessibleStatusTextBox { AccessibleName = "Clone status", Multiline = true, ReadOnly = true, Height = 90, ScrollBars = ScrollBars.Vertical, TabStop = true, Text = "Ready. Add samples and confirm consent before creating a voice." };
                var announcement = new AccessibleStatusLabel { AutoSize = true, AccessibleName = "Clone upload status", Text = "Ready" };
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
                var close = Ui.Button("Cl&ose", () => f.Close());
                var cancel = Ui.Button("Cancel &upload", () => { if (cancellation != null) { cancellation.Cancel(); status.UpdateReadableText("Cancelling the request..."); SetCloneStatus(announcement, status.Text); } }); cancel.Enabled = false;
                bottom.Controls.Add(close); bottom.Controls.Add(cancel);
                var statusPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
                status.Dock = DockStyle.Top; statusPanel.Controls.Add(status); statusPanel.Controls.Add(announcement);
                Action<string> report = message => { if (f.IsDisposed || !busy) return; status.UpdateReadableText(message); SetCloneStatus(announcement, message); };
                create.Click += async delegate
                {
                    if (busy) return; if (!consent.Checked || paths.Count == 0 || string.IsNullOrWhiteSpace(name.Text)) { Ui.Result(f, "Cannot clone", "Enter a name, add samples, and confirm that you have the rights and consent."); return; }
                    if (MessageBox.Show(f, "Upload these voice samples and create a voice in your account?", "Create voice", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    var fields = new Dictionary<string, string> { { "name", name.Text }, { "description", description.Text } }; var files = paths.Select(x => new UploadFile("files", x)).ToList();
                    cancellation = new CancellationTokenSource(); busy = true; p.Enabled = false; close.Enabled = false; cancel.Enabled = true; f.CancelButton = cancel;
                    report("Preparing voice samples for upload..."); var progress = new Progress<string>(report);
                    try
                    {
                        await Task.Run(() => client.Upload("/v1/voices/add", fields, files, cancellation.Token, message => ((IProgress<string>)progress).Report(message)));
                        report("Voice created successfully. It has been added to your account."); create.Enabled = false;
                    }
                    catch (OperationCanceledException) { report("Request cancelled. If the upload had already reached ElevenLabs, the voice may still have been created. Check your voice library before trying again."); }
                    catch (Exception ex) { report("Could not create the voice. " + ex.Message + " Check your voice library before retrying an upload that may already have reached ElevenLabs."); }
                    finally { busy = false; cancellation.Dispose(); cancellation = null; if (!f.IsDisposed) { p.Enabled = true; close.Enabled = true; cancel.Enabled = false; f.CancelButton = close; } }
                };
                Ui.Row(p, "", create); f.Controls.Add(p); f.Controls.Add(statusPanel); f.Controls.Add(bottom); f.CancelButton = close;
                f.FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) { e.Cancel = true; cancel.PerformClick(); } }; f.ShowDialog(owner);
            }
        }
        private static void SetCloneStatus(AccessibleStatusLabel label, string message)
        {
            label.Text = message; label.NotifyNameChanged();
        }
    }
}
