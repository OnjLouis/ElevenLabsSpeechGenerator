using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class PreferencesForm : Form
    {
        public PreferencesForm(AppSettings settings, int initialTab = 0)
        {
            Text = "Preferences"; AccessibleName = Text; Size = new Size(700, 500); MinimumSize = new Size(600, 420); ShowInTaskbar = false; ShowIcon = false; MinimizeBox = MaximizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Preference categories" };
            var general = new TabPage("General"); var api = new TabPage("API key"); var updates = new TabPage("Updates"); tabs.TabPages.AddRange(new[] { general, api, updates });
            var g = Ui.Layout(2); var folder = Ui.Text("Default output folder", false); folder.Text = settings.DefaultOutputFolder;
            g.AutoSize = true; g.Dock = DockStyle.Top;
            var folderRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true }; folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); folder.Dock = DockStyle.Fill; folderRow.Controls.Add(folder);
            folderRow.Controls.Add(Ui.Button("&Browse...", () => { using (var d = new FolderBrowserDialog { SelectedPath = AppSettings.NormalizeFolderInput(folder.Text) }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; }));
            Ui.Row(g, "Default &output folder:", folderRow);
            var sound = new CheckBox { Text = "Play a &completion sound", Checked = settings.CompletionSound, AutoSize = true };
            var details = new CheckBox { Text = "Save &generation details", Checked = settings.SaveDetails, AutoSize = true };
            Ui.Row(g, "", sound); Ui.Row(g, "", details); general.Controls.Add(g);
            var audio = new TabPage("Audio"); tabs.TabPages.Add(audio);
            var audioLayout = Ui.Layout(2); audioLayout.AutoSize = true; audioLayout.Dock = DockStyle.Top;
            var autoPlay = new CheckBox { Text = "Play new &generations automatically in sequence", AccessibleName = "Play new generations automatically in sequence", Checked = settings.AutoPlayGenerations, AutoSize = true };
            var playbackDevice = AudioDevicesForm.DeviceList(settings.PlaybackDevice);
            var outputFormat = Ui.Combo("Default output format", SpeechProject.OutputFormats.Cast<object>().ToArray()); outputFormat.SelectedItem = settings.DefaultOutputFormat;
            Ui.Row(audioLayout, "Playback &device:", playbackDevice); Ui.Row(audioLayout, "Default &format:", outputFormat); Ui.Row(audioLayout, "", autoPlay); audio.Controls.Add(audioLayout);
            var a = Ui.Layout(2); var key = Ui.Text("API key", false); key.UseSystemPasswordChar = true; key.Text = AppPaths.LoadApiKey();
            Ui.Row(a, "API &key:", key); var show = new CheckBox { Text = "&Show API key", AutoSize = true }; show.CheckedChanged += delegate { key.UseSystemPasswordChar = !show.Checked; }; Ui.Row(a, "", show);
            CancellationTokenSource testCancellation = null;
            var test = Ui.Button("&Test API key", () => { });
            test.Click += async delegate
            {
                test.Enabled = false; key.Enabled = false; testCancellation = new CancellationTokenSource(); var client = new SpeechClient(key.Text); var result = new System.Text.StringBuilder();
                try
                {
                    foreach (var pair in new[] { new[] { "Models", "/v1/models" }, new[] { "Voices", "/v2/voices?page_size=1" }, new[] { "Credit balance", "/v1/user/subscription" } })
                    {
                        try { await Task.Run(() => client.Get(pair[1], testCancellation.Token)); result.AppendLine(pair[0] + ": available."); }
                        catch (OperationCanceledException) { throw; } catch (Exception ex) { result.AppendLine(pair[0] + ": " + ex.Message); }
                    }
                    result.AppendLine("No audio was generated. Grant the appropriate generation permissions for the modes you use.");
                    if (!IsDisposed) Ui.Result(this, "API key test result", result.ToString());
                }
                catch (OperationCanceledException) { }
                finally { if (!IsDisposed) { test.Enabled = true; key.Enabled = true; } testCancellation.Dispose(); testCancellation = null; }
            };
            Ui.Row(a, "", test); var link = new LinkLabel { Text = "Get an ElevenLabs API key", AutoSize = true }; link.LinkClicked += delegate { Process.Start("https://elevenlabs.io/app/settings/api-keys"); }; Ui.Row(a, "", link);
            Ui.Row(a, "", new Label { Text = "Your key is encrypted for your Windows account. The test checks access without spending generation credits.", AutoSize = true, MaximumSize = new Size(500, 0) }); api.Controls.Add(a);
            var u = Ui.Layout(2); var frequency = Ui.Combo("Check for updates", new object[] { "Startup", "Daily", "Weekly", "Never" }); frequency.SelectedItem = settings.UpdateCheckFrequency;
            var silent = new CheckBox { Text = "Install verified updates &silently", Checked = settings.InstallUpdatesSilently, AutoSize = true }; Ui.Row(u, "&Check for updates:", frequency); Ui.Row(u, "", silent); updates.Controls.Add(u);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var cancel = Ui.Button("&Cancel", () => { DialogResult = DialogResult.Cancel; }); cancel.TabIndex = 1;
            var ok = Ui.Button("&OK", () =>
            {
                string full;
                try
                {
                    full = AppSettings.ResolveOutputFolder(folder.Text, true);
                    Directory.CreateDirectory(full);
                    folder.Text = full;
                }
                catch (Exception ex)
                {
                    tabs.SelectedTab = general;
                    Ui.Result(this, "Could not use output folder", "Default output folder could not be used." + Environment.NewLine + Environment.NewLine + ex.Message);
                    folder.Focus();
                    return;
                }
                try { AppPaths.SaveApiKey(key.Text); }
                catch (Exception ex)
                {
                    tabs.SelectedTab = api;
                    Ui.Result(this, "Could not save API key", ex.Message);
                    key.Focus();
                    return;
                }
                try
                {
                    settings.DefaultOutputFolder = full; settings.CompletionSound = sound.Checked; settings.SaveDetails = details.Checked; settings.AutoPlayGenerations = autoPlay.Checked; settings.PlaybackDevice = playbackDevice.SelectedIndex - 1; settings.DefaultOutputFormat = (string)outputFormat.SelectedItem; settings.UpdateCheckFrequency = (string)frequency.SelectedItem; settings.InstallUpdatesSilently = silent.Checked; settings.Save(); DialogResult = DialogResult.OK;
                }
                catch (Exception ex) { Ui.Result(this, "Could not save preferences", ex.Message); }
            }); ok.TabIndex = 0; buttons.Controls.Add(cancel); buttons.Controls.Add(ok); Controls.Add(tabs); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
            FormClosing += delegate { if (testCancellation != null) testCancellation.Cancel(); };
            tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabCount - 1, initialTab));
        }
    }
}
