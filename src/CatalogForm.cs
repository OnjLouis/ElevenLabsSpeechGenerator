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
    internal sealed class CatalogForm : Form
    {
        private readonly string kind;
        private readonly SpeechClient client;
        private readonly AppSettings settings;
        private readonly Playback playback;
        private readonly SpeechProject project;
        private readonly TextBox search = Ui.Text("Search", false);
        private readonly CheckBox shared = new CheckBox { Text = "Search &shared voice library", AutoSize = true };
        private readonly ListBox list;
        private readonly TextBox description = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Details", Dock = DockStyle.Fill };
        private readonly List<NamedItem> items = new List<NamedItem>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly List<string> previews = new List<string>();
        private string cursor = "";
        private bool busy;
        public NamedItem SelectedVoice { get; private set; }
        public CatalogForm(string kind, SpeechClient client, AppSettings settings, Playback playback, SpeechProject project)
        {
            this.kind = kind; this.client = client; this.settings = settings; this.playback = playback; this.project = project;
            list = kind == "Voices" ? new VoiceListBox() : new ListBox();
            list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.AccessibleName = "Items";
            Text = kind == "Voices" ? "Voice library" : kind == "History" ? "Generation history" : "Pronunciation dictionaries"; AccessibleName = Text; Size = new Size(850, 600); MinimumSize = new Size(650, 450); ShowInTaskbar = false; ShowIcon = false; StartPosition = FormStartPosition.CenterParent;
            if (kind == "Dictionaries") list.SelectionMode = SelectionMode.MultiExtended;
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) }; p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; row.Controls.Add(new Label { Text = "&Search:", AutoSize = true }); row.Controls.Add(search); row.Controls.Add(shared); shared.Visible = kind == "Voices";
            row.Controls.Add(Ui.Button("&Refresh", async () => await LoadItems(false))); row.Controls.Add(Ui.Button("&Next page", async () => await LoadItems(true))); p.Controls.Add(row, 0, 0); p.Controls.Add(list, 0, 1); p.Controls.Add(description, 0, 2);
            list.SelectedIndexChanged += delegate { var x = list.SelectedItem as NamedItem; description.Text = x == null ? "" : Describe(x); };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            if (kind == "Voices") { actions.Controls.Add(Ui.Button("&Use voice", UseVoice)); actions.Controls.Add(Ui.Button("&Preview", async () => await Preview())); actions.Controls.Add(Ui.Button("&Delete voice", async () => await DeleteVoice())); }
            else if (kind == "History") { actions.Controls.Add(Ui.Button("&Download", async () => await DownloadHistory())); actions.Controls.Add(Ui.Button("&Play", async () => await DownloadHistory(true))); }
            else { actions.Controls.Add(Ui.Button("&Use selected", ApplyDictionaries)); actions.Controls.Add(Ui.Button("&Create...", CreateDictionary)); actions.Controls.Add(Ui.Button("&Import PLS...", async () => await ImportDictionary())); actions.Controls.Add(Ui.Button("&Export PLS...", async () => await ExportDictionary())); actions.Controls.Add(Ui.Button("Add &rule...", async () => await AddRule())); }
            p.Controls.Add(actions, 0, 3); Controls.Add(p); Ui.CloseButton(this);
            Shown += async delegate { await LoadItems(false); };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) { cancellation.Cancel(); e.Cancel = true; } };
            FormClosed += delegate { playback.Stop(); foreach (var path in previews) try { File.Delete(path); } catch (IOException) { } cancellation.Dispose(); };
        }
        private async Task Work(Func<CancellationToken, Task> action)
        {
            if (busy || cancellation.IsCancellationRequested) return; busy = true;
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) Ui.Result(this, Text, ex.Message); }
            finally { busy = false; if (cancellation.IsCancellationRequested && !IsDisposed) Close(); }
        }
        private async Task LoadItems(bool next)
        {
            if (next && cursor.Length == 0) return;
            var query = search.Text; bool isShared = shared.Checked; string page = next ? cursor : "";
            await Work(async token =>
            {
                string path = kind == "Voices" ? isShared ? "/v1/shared-voices?page_size=100&search=" + Uri.EscapeDataString(query) + (page.Length > 0 ? "&page=" + page : "") : "/v2/voices?page_size=100&search=" + Uri.EscapeDataString(query) + (page.Length > 0 ? "&next_page_token=" + Uri.EscapeDataString(page) : "") : kind == "History" ? "/v1/history?page_size=100" + (page.Length > 0 ? "&start_after_history_item_id=" + Uri.EscapeDataString(page) : "") : "/v1/pronunciation-dictionaries?page_size=100" + (page.Length > 0 ? "&cursor=" + Uri.EscapeDataString(page) : "");
                var d = JsonData.Object(await Task.Run(() => client.Get(path, token))); var source = JsonData.Items(d, kind == "History" ? "history" : kind == "Dictionaries" ? "pronunciation_dictionaries" : "voices").ToList(); items.Clear();
                foreach (var x in source)
                {
                    string name = JsonData.String(x, "name"); if (kind == "History") name = JsonData.String(x, "voice_name") + " - " + JsonData.String(x, "text").Replace("\n", " ");
                    if (kind != "Voices" && query.Length > 0 && name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    items.Add(new NamedItem { Id = JsonData.String(x, kind == "History" ? "history_item_id" : kind == "Dictionaries" ? "id" : "voice_id"), Name = name, Data = x });
                }
                cursor = kind == "History" ? JsonData.Bool(d, "has_more") && source.Count > 0 ? JsonData.String(source.Last(), "history_item_id") : "" : kind == "Dictionaries" ? JsonData.String(d, "next_cursor") : isShared ? JsonData.Bool(d, "has_more") ? (page.Length == 0 ? "1" : (int.Parse(page) + 1).ToString()) : "" : JsonData.String(d, "next_page_token");
                var voiceList = list as VoiceListBox; if (voiceList != null) voiceList.ResetSearch();
                list.Items.Clear(); list.Items.AddRange(items.Cast<object>().ToArray()); if (items.Count > 0) list.SelectedIndex = 0;
            });
        }
        private sealed class VoiceListBox : ListBox
        {
            // Keep native list accessibility, but accumulate letters instead of jumping for each one.
            private const int SearchPauseMilliseconds = 1000;
            private readonly System.Diagnostics.Stopwatch sinceTyping = new System.Diagnostics.Stopwatch();
            private string prefix = "";

            public void ResetSearch() { prefix = ""; sinceTyping.Reset(); }

            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                if (char.IsControl(e.KeyChar) || (ModifierKeys & (Keys.Control | Keys.Alt)) != Keys.None)
                {
                    base.OnKeyPress(e); return;
                }
                e.Handled = true;
                if (sinceTyping.ElapsedMilliseconds > SearchPauseMilliseconds) ResetSearch();
                bool cycle = prefix.Length == 1 && string.Equals(prefix, e.KeyChar.ToString(), StringComparison.CurrentCultureIgnoreCase);
                bool fresh = prefix.Length == 0;
                if (!cycle) prefix += e.KeyChar;
                sinceTyping.Restart();
                SelectPrefix(fresh || cycle);
            }

            private void SelectPrefix(bool advance)
            {
                if (Items.Count == 0 || prefix.Length == 0) return;
                int start = advance ? SelectedIndex : SelectedIndex - 1;
                int match = FindString(prefix, Math.Max(-1, start));
                if (match >= 0) SelectedIndex = match;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Back && !e.Control && !e.Alt)
                {
                    if (sinceTyping.ElapsedMilliseconds > SearchPauseMilliseconds) ResetSearch();
                    if (prefix.Length > 0) prefix = prefix.Substring(0, prefix.Length - 1);
                    sinceTyping.Restart(); SelectPrefix(false); e.SuppressKeyPress = true; return;
                }
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                    e.KeyCode == Keys.Home || e.KeyCode == Keys.End || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Escape)
                    ResetSearch();
                base.OnKeyDown(e);
            }

            protected override void OnLeave(EventArgs e) { ResetSearch(); base.OnLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { ResetSearch(); base.OnMouseDown(e); }
        }

        private string Describe(NamedItem item)
        {
            if (kind == "History") return SpeechProject.WindowsLines(JsonData.String(item.Data, "text")) + "\r\nModel: " + JsonData.String(item.Data, "model_id");
            if (kind == "Dictionaries") return JsonData.String(item.Data, "description") + "\r\nVersion: " + JsonData.String(item.Data, "latest_version_id");
            return SpeechProject.WindowsLines(JsonData.String(item.Data, "description")) + "\r\nCategory: " + JsonData.String(item.Data, "category");
        }
        private async void UseVoice()
        {
            var x = list.SelectedItem as NamedItem; if (x == null) return;
            if (shared.Checked)
            {
                if (MessageBox.Show(this, "Add this shared voice to your ElevenLabs account?", "Add voice", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                await Work(async token => { var d = JsonData.Object(await Task.Run(() => client.Post("/v1/voices/add/" + Uri.EscapeDataString(JsonData.String(x.Data, "public_owner_id")) + "/" + Uri.EscapeDataString(x.Id), new { new_name = x.Name }, token))); SelectedVoice = new NamedItem { Id = JsonData.String(d, "voice_id"), Name = x.Name, Data = x.Data }; });
            }
            else SelectedVoice = x;
            if (SelectedVoice != null) Close();
        }
        private async Task Preview()
        {
            var uri = VoicePreview.Url(list.SelectedItem as NamedItem);
            if (uri == null) { Ui.Result(this, "Voice preview", "No preview is available for this voice."); return; }
            await Work(async token =>
            {
                var path = Path.Combine(AppPaths.UserFolder, "Preview-" + Guid.NewGuid().ToString("N") + ".mp3"); previews.Add(path);
                await Task.Run(() => VoicePreview.Download(uri, path, token)); playback.Play(path, settings.PlaybackDevice);
            });
        }
        private async Task DeleteVoice()
        {
            var x = list.SelectedItem as NamedItem; if (x == null || shared.Checked) return;
            if (MessageBox.Show(this, "Permanently delete '" + x.Name + "' from your ElevenLabs account?", "Delete voice", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Work(async token => { await Task.Run(() => client.Delete("/v1/voices/" + Uri.EscapeDataString(x.Id), token)); list.Items.Remove(x); });
        }
        private async Task DownloadHistory(bool play = false)
        {
            var x = list.SelectedItem as NamedItem; if (x == null) return;
            var path = FileNames.Next(settings.DefaultOutputFolder, x.Name.Length > 0 ? x.Name : "History", ".mp3"); Directory.CreateDirectory(settings.DefaultOutputFolder);
            if (!play) using (var d = new SaveFileDialog { Filter = "MP3|*.mp3", FileName = Path.GetFileName(path), InitialDirectory = settings.DefaultOutputFolder }) { if (d.ShowDialog(this) != DialogResult.OK) return; path = d.FileName; if (File.Exists(path)) { Ui.Result(this, "Choose another filename", "Choose a new filename to keep the existing file."); return; } }
            await Work(async token => { await Task.Run(() => client.Download("/v1/history/" + Uri.EscapeDataString(x.Id) + "/audio", path, token)); if (play) playback.Play(path, settings.PlaybackDevice); });
        }
        private void ApplyDictionaries()
        {
            if (list.SelectedItems.Count > 3) { Ui.Result(this, "Pronunciation dictionaries", "Select up to three dictionaries."); return; }
            project.Dictionaries = list.SelectedItems.Cast<NamedItem>().Select(x => new Dictionary<string, object> { { "pronunciation_dictionary_id", x.Id }, { "version_id", JsonData.String(x.Data, "latest_version_id") } }).ToList(); Close();
        }
        private async void CreateDictionary()
        {
            string name = ToolDialogs.Ask(this, "Create dictionary", "Dictionary &name:", ""); if (string.IsNullOrWhiteSpace(name)) return;
            var rule = Rule(); if (rule == null) return;
            await Work(async token => { await Task.Run(() => client.Post("/v1/pronunciation-dictionaries/add-from-rules", new { name = name, rules = new[] { rule } }, token)); }); await LoadItems(false);
        }
        private Dictionary<string, object> Rule()
        {
            using (var f = Ui.Dialog("Pronunciation rule", new Size(650, 400)))
            {
                var p = Ui.Layout(2); var type = Ui.Combo("Rule type", new object[] { "Alias", "IPA phoneme", "CMU phoneme" }); var original = Ui.Text("Original text", false); var replace = Ui.Text("Pronunciation", false); Ui.Row(p, "&Type:", type); Ui.Row(p, "&Original text:", original); Ui.Row(p, "&Pronunciation:", replace); f.Controls.Add(p);
                var ok = Ui.Button("&OK", () => { if (!string.IsNullOrWhiteSpace(original.Text) && !string.IsNullOrWhiteSpace(replace.Text)) f.DialogResult = DialogResult.OK; }); ok.Dock = DockStyle.Bottom; f.Controls.Add(ok); Ui.CloseButton(f);
                if (f.ShowDialog(this) != DialogResult.OK) return null;
                return type.SelectedIndex == 0 ? new Dictionary<string, object> { { "type", "alias" }, { "string_to_replace", original.Text }, { "alias", replace.Text } } : new Dictionary<string, object> { { "type", "phoneme" }, { "string_to_replace", original.Text }, { "phoneme", replace.Text }, { "alphabet", type.SelectedIndex == 1 ? "ipa" : "cmu" } };
            }
        }
        private async Task AddRule()
        {
            var x = list.SelectedItem as NamedItem; if (x == null) return; var rule = Rule(); if (rule == null) return;
            await Work(async token => { await Task.Run(() => client.Post("/v1/pronunciation-dictionaries/" + Uri.EscapeDataString(x.Id) + "/add-rules", new { rules = new[] { rule } }, token)); }); await LoadItems(false);
        }
        private async Task ImportDictionary()
        {
            using (var f = new OpenFileDialog { Filter = "Pronunciation lexicon|*.pls" })
            {
                if (f.ShowDialog(this) != DialogResult.OK) return; var path = f.FileName;
                var name = ToolDialogs.Ask(this, "Import dictionary", "Dictionary &name:", Path.GetFileNameWithoutExtension(path)); if (string.IsNullOrWhiteSpace(name)) return;
                await Work(async token => { await Task.Run(() => client.Upload("/v1/pronunciation-dictionaries/add-from-file", new Dictionary<string, string> { { "name", name } }, new List<UploadFile> { new UploadFile("file", path) }, token)); }); await LoadItems(false);
            }
        }
        private async Task ExportDictionary()
        {
            var x = list.SelectedItem as NamedItem; if (x == null) return;
            using (var f = new SaveFileDialog { Filter = "Pronunciation lexicon|*.pls", FileName = FileNames.Stem(x.Name) + ".pls" })
            {
                if (f.ShowDialog(this) != DialogResult.OK) return; var path = f.FileName;
                await Work(async token => { var stage = path + "." + Guid.NewGuid().ToString("N") + ".download"; try { await Task.Run(() => client.Download("/v1/pronunciation-dictionaries/" + Uri.EscapeDataString(x.Id) + "/" + Uri.EscapeDataString(JsonData.String(x.Data, "latest_version_id")) + "/download", stage, token)); if (File.Exists(path)) File.Replace(stage, path, null); else File.Move(stage, path); } finally { if (File.Exists(stage)) File.Delete(stage); } });
            }
        }
    }
}
