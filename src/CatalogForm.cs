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
        private readonly CheckBox shared = new CheckBox { Text = "Search shared voice &library", AutoSize = true };
        private readonly ListBox list;
        private readonly TextBox description = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Details", Dock = DockStyle.Fill };
        private readonly List<NamedItem> items = new List<NamedItem>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly VoiceAudition audition;
        private readonly CheckBox autoPreview = new CheckBox { Text = "&Automatically preview selected voices", AutoSize = true, AccessibleName = "Automatically preview selected voices" };
        private Button renameVoice;
        private bool replacingItems;
        private readonly CatalogPagination pages = new CatalogPagination();
        private readonly Button previousPage;
        private readonly Button nextPage;
        private bool busy;
        public NamedItem SelectedVoice { get; private set; }
        public readonly Dictionary<string, string> RenamedVoices = new Dictionary<string, string>();
        public CatalogForm(string kind, SpeechClient client, AppSettings settings, Playback playback, SpeechProject project)
        {
            this.kind = kind; this.client = client; this.settings = settings; this.playback = playback; this.project = project;
            audition = new VoiceAudition(AppPaths.UserFolder, (uri, path, token) => Task.Run(() => VoicePreview.Download(uri, path, token)),
                path => playback.Play(path, settings.PlaybackDevice), playback.Stop, () => playback.IsPlaying,
                ex => { if (!IsDisposed) Ui.Result(this, "Voice preview", ex.Message); });
            list = kind == "Voices" ? new PrefixListBox() : new ListBox();
            list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.AccessibleName = "Items";
            Text = kind == "Voices" ? "Voice library" : kind == "History" ? "Generation history" : "Pronunciation dictionaries"; AccessibleName = Text; Size = new Size(850, 600); MinimumSize = new Size(650, 450); ShowInTaskbar = false; ShowIcon = false; StartPosition = FormStartPosition.CenterParent;
            if (kind == "Dictionaries") list.SelectionMode = SelectionMode.MultiExtended;
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) }; p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; row.Controls.Add(new Label { Text = "&Search:", AutoSize = true }); row.Controls.Add(search); row.Controls.Add(shared); shared.Visible = kind == "Voices";
            row.Controls.Add(Ui.Button("&Refresh", async () => await LoadItems(false)));
            previousPage = Ui.Button("Pre&vious page", async () => await LoadPage(CatalogPageMove.Previous));
            nextPage = Ui.Button("&Next page", async () => await LoadItems(true));
            row.Controls.Add(previousPage); row.Controls.Add(nextPage);
            search.TextChanged += delegate { UpdateRenameAvailability(); };
            shared.CheckedChanged += delegate { UpdateRenameAvailability(); };
            p.Controls.Add(row, 0, 0); p.Controls.Add(list, 0, 1); p.Controls.Add(description, 0, 2);
            list.SelectedIndexChanged += delegate {
                var x = list.SelectedItem as NamedItem; description.Text = x == null ? "" : Describe(x);
                if (renameVoice != null) renameVoice.Enabled = !busy && !shared.Checked && VoiceNames.CanRename(x);
                if (kind == "Voices" && !replacingItems) audition.Select(x, autoPreview.Checked);
            };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            if (kind == "Voices") {
                autoPreview.Checked = settings.AutoPreviewVoices;
                autoPreview.CheckedChanged += delegate {
                    try { settings.AutoPreviewVoices = autoPreview.Checked; settings.Save(); }
                    catch (Exception ex) { Ui.Result(this, "Could not save preview preference", ex.Message); }
                    audition.Select(list.SelectedItem as NamedItem, autoPreview.Checked);
                };
                shared.CheckedChanged += delegate { audition.Stop(); renameVoice.Enabled = !shared.Checked && VoiceNames.CanRename(list.SelectedItem as NamedItem); };
                actions.Controls.Add(Ui.Button("&Use voice", UseVoice));
                actions.Controls.Add(Ui.Button("&Preview / Stop", () => audition.Toggle(list.SelectedItem as NamedItem)));
                actions.Controls.Add(autoPreview);
                renameVoice = Ui.Button("R&ename voice...", async () => await RenameVoice()); renameVoice.Enabled = false; actions.Controls.Add(renameVoice);
                actions.Controls.Add(Ui.Button("&Delete voice", async () => await DeleteVoice()));
            }
            else if (kind == "History") { actions.Controls.Add(Ui.Button("&Download", async () => await DownloadHistory())); actions.Controls.Add(Ui.Button("&Play", async () => await DownloadHistory(true))); }
            else { actions.Controls.Add(Ui.Button("&Use selected", ApplyDictionaries)); actions.Controls.Add(Ui.Button("&Create...", CreateDictionary)); actions.Controls.Add(Ui.Button("&Import PLS...", async () => await ImportDictionary())); actions.Controls.Add(Ui.Button("&Export PLS...", async () => await ExportDictionary())); actions.Controls.Add(Ui.Button("Add &rule...", async () => await AddRule())); }
            p.Controls.Add(actions, 0, 3); Controls.Add(p); Ui.CloseButton(this);
            Shown += async delegate { await LoadItems(false); };
            UpdateRenameAvailability();
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) { cancellation.Cancel(); e.Cancel = true; } };
            FormClosed += delegate { audition.Dispose(); playback.Stop(); cancellation.Dispose(); };
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (kind == "Voices" && keyData == Keys.Enter && list.Focused) { audition.Toggle(list.SelectedItem as NamedItem); return true; }
            return base.ProcessDialogKey(keyData);
        }
        private async Task Work(Func<CancellationToken, Task> action)
        {
            if (busy || cancellation.IsCancellationRequested) return; busy = true; UpdateRenameAvailability();
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) Ui.Result(this, Text, ex.Message); }
            finally { busy = false; UpdateRenameAvailability(); if (cancellation.IsCancellationRequested && !IsDisposed) Close(); }
        }
        private void UpdateRenameAvailability()
        {
            if (IsDisposed) return;
            previousPage.Enabled = !busy && pages.CanPrevious(PageContext);
            nextPage.Enabled = !busy && pages.CanNext(PageContext);
            if (renameVoice != null && !IsDisposed) renameVoice.Enabled = !busy && !shared.Checked && VoiceNames.CanRename(list.SelectedItem as NamedItem);
        }
        private string PageContext { get { return kind + "\n" + shared.Checked + "\n" + search.Text; } }
        private async Task LoadItems(bool next)
        {
            await LoadPage(next ? CatalogPageMove.Next : CatalogPageMove.Refresh);
        }
        private async Task LoadPage(CatalogPageMove move)
        {
            if (busy || cancellation.IsCancellationRequested) return;
            var scope = PageContext; string page = pages.Request(move, scope);
            if (page == null) return;
            if (kind == "Voices") audition.Stop();
            var query = search.Text; bool isShared = shared.Checked;
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
                string following = !JsonData.Bool(d, "has_more") ? "" : kind == "History" ? JsonData.String(d, "last_history_item_id") : kind == "Dictionaries" ? JsonData.String(d, "next_cursor") : isShared ? (page.Length == 0 ? "1" : (int.Parse(page) + 1).ToString()) : JsonData.String(d, "next_page_token");
                if (kind == "History" && JsonData.Bool(d, "has_more") && following.Length == 0 && source.Count > 0) following = JsonData.String(source.Last(), "history_item_id");
                pages.Commit(move, scope, page, following);
                var voiceList = list as PrefixListBox; if (voiceList != null) voiceList.ResetSearch();
                replacingItems = true;
                try { list.Items.Clear(); list.Items.AddRange(items.Cast<object>().ToArray()); if (items.Count > 0) list.SelectedIndex = 0; }
                finally { replacingItems = false; }
                if (kind == "Voices") audition.Select(list.SelectedItem as NamedItem, autoPreview.Checked);
            });
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
        private async Task RenameVoice()
        {
            var voice = list.SelectedItem as NamedItem;
            if (shared.Checked || !VoiceNames.CanRename(voice)) return;
            var value = ToolDialogs.Ask(this, "Rename voice", "Voice &name:", voice.Name);
            if (value == null) return;
            string name;
            try { name = VoiceNames.Validate(value); } catch (Exception ex) { Ui.Result(this, "Could not rename voice", ex.Message); return; }
            if (name == voice.Name) return;
            await Work(async token =>
            {
                await Task.Run(() => VoiceNames.Rename(client, voice, name, token));
                voice.Name = name; voice.Data["name"] = name; RenamedVoices[voice.Id] = name;
                replacingItems = true;
                try { var index = list.Items.IndexOf(voice); var selected = list.SelectedIndex; if (index >= 0) list.Items[index] = voice; list.SelectedIndex = selected; }
                finally { replacingItems = false; }
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
