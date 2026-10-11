using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class VoiceLibraryTests
    {
        private static int checks;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        private static NamedItem Voice(string id) { return new NamedItem { Id = id, Name = id, Data = new Dictionary<string, object> { { "preview_url", "https://example.invalid/" + id }, { "is_owner", true }, { "category", "cloned" } } }; }
        private static void Wait(Task task)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && clock.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            Check(task.IsCompleted, "Voice audition did not finish within its test deadline."); task.GetAwaiter().GetResult();
        }
        public static int Run()
        {
            checks = 0;
            Check(!new AppSettings().AutoPreviewVoices, "Automatic preview must default to off.");
            var settings = new AppSettings { AutoPreviewVoices = true }; settings.Save();
            Check(AppSettings.Load().AutoPreviewVoices, "Automatic preview must survive restart.");
            Check(SpeechTags.Examples.Length >= 70 && SpeechTags.Examples.Distinct(StringComparer.OrdinalIgnoreCase).Count() == SpeechTags.Examples.Length, "Documented tags must be expanded without duplicates.");
            foreach (var tag in SpeechTags.Examples) Check(SpeechTags.Normalize(tag) == tag, "Invalid built-in tag: " + tag);
            var own = Voice("own");
            Check(VoiceNames.CanRename(own), "Owned cloned voices must be renameable.");
            own.Data["is_owner"] = false; Check(!VoiceNames.CanRename(own), "Shared voices must not be renameable.");
            own.Data.Remove("is_owner"); Check(!VoiceNames.CanRename(own), "Unknown ownership must not allow rename.");
            own.Data["is_owner"] = true; own.Data["category"] = "premade"; Check(!VoiceNames.CanRename(own), "Default voices must not be renameable.");
            Check(VoiceNames.Validate("  New name  ") == "New name", "Rename must trim outer whitespace.");
            foreach (var name in new[] { "", "  ", "two\nlines" }) { bool rejected = false; try { VoiceNames.Validate(name); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Invalid voice name accepted."); }

            string folder = Path.Combine(AppPaths.AppFolder, "AuditionFixtures");
            Directory.CreateDirectory(folder);
            var requests = new List<TaskCompletionSource<int>>();
            var played = new List<string>(); var errors = new List<Exception>(); bool playing = false;
            Func<Uri, string, CancellationToken, Task> download = async (url, path, token) => {
                var gate = new TaskCompletionSource<int>(); requests.Add(gate); await gate.Task;
                File.WriteAllText(path, url.AbsolutePath);
            };
            using (var audition = new VoiceAudition(folder, download, path => { played.Add(File.ReadAllText(path)); playing = true; }, () => playing = false, () => playing, errors.Add))
            {
                audition.Toggle(Voice("old")); var old = audition.LastRequest;
                audition.Toggle(Voice("new")); var newest = audition.LastRequest;
                requests[1].SetResult(0); Wait(newest);
                requests[0].SetResult(0); Wait(old);
                Check(played.SequenceEqual(new[] { "/new" }) && Directory.GetFiles(folder).Length == 1, "A stale preview played or left its file behind.");
                audition.Toggle(Voice("new"));
                Check(!playing && Directory.GetFiles(folder).Length == 0, "Repeated preview must stop and release its file.");
                audition.Toggle(Voice("new")); var pending = audition.LastRequest; audition.Toggle(Voice("new"));
                requests.Last().SetResult(0); Wait(pending);
                Check(played.Count == 1 && Directory.GetFiles(folder).Length == 0, "Stopping a download must prevent later playback.");
                audition.Select(Voice("first"), true); var first = audition.LastRequest;
                audition.Select(Voice("last"), true); var last = audition.LastRequest;
                audition.Select(null, false); Wait(first); Wait(last);
                Check(requests.Count == 3, "Rapid selection should cancel previews before downloading.");
                audition.Select(new NamedItem { Id = "missing" }, true);
                Check(errors.Count == 0, "Automatic browsing must skip unavailable previews quietly.");
                audition.Toggle(new NamedItem { Id = "missing" }); Check(errors.Count == 1, "Manual missing previews need an explanation.");
                audition.Toggle(Voice("closed")); var closed = audition.LastRequest; audition.Dispose(); requests.Last().SetResult(0); Wait(closed);
                Check(played.Count == 1 && Directory.GetFiles(folder).Length == 0, "Closing the library must cancel previews and clean files.");
            }
            using (var audition = new VoiceAudition(folder, (url, path, token) => { File.WriteAllText(path, "preview"); return Task.FromResult(0); }, path => playing = true, () => playing = false, () => playing, errors.Add))
            {
                audition.Select(Voice("auto"), true); Wait(audition.LastRequest); Check(playing, "Automatic selection did not play.");
                playing = false; audition.Toggle(Voice("auto")); Wait(audition.LastRequest); Check(playing, "A finished preview must replay on demand.");
            }
            Check(Directory.GetFiles(folder).Length == 0, "Audition files leaked after disposal.");
            Directory.Delete(folder);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var player = new Playback())
            using (var form = new CatalogForm("Voices", new SpeechClient(""), new AppSettings(), player, new SpeechProject()))
            using (var host = new Form { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000) })
            using (var fake = new VoiceAudition(AppPaths.UserFolder, (url, path, token) => Task.FromResult(0), path => playing = true, () => playing = false, () => playing, errors.Add))
            {
                typeof(CatalogForm).GetField("audition", flags).SetValue(form, fake);
                var list = (ListBox)typeof(CatalogForm).GetField("list", flags).GetValue(form);
                host.Controls.Add(list); host.Show(); list.Items.Add(Voice("keyboard")); list.SelectedIndex = 0; list.Focus();
                var enter = typeof(CatalogForm).GetMethod("ProcessDialogKey", flags);
                Check((bool)enter.Invoke(form, new object[] { Keys.Enter }) && playing && list.Focused, "Enter must preview without moving voice-list focus.");
                enter.Invoke(form, new object[] { Keys.Enter }); Check(!playing && list.Focused, "Enter again must stop without moving focus.");
                var preview = All(form).OfType<Button>().Single(x => x.Text == "&Preview / Stop");
                Check(string.Equals(preview.AccessibilityObject.KeyboardShortcut, "Alt+P", StringComparison.OrdinalIgnoreCase), "Preview must keep Alt+P.");
                Check(All(form).OfType<CheckBox>().Any(x => x.AccessibleName == "Automatically preview selected voices"), "Automatic preview needs a named native checkbox.");
                var automatic = All(form).OfType<CheckBox>().Single(x => x.AccessibleName == "Automatically preview selected voices");
                Check(preview.Parent == automatic.Parent && preview.Parent.Controls.GetChildIndex(automatic) == preview.Parent.Controls.GetChildIndex(preview) + 1, "Automatic preview must follow Preview / Stop directly.");
                var rename = All(form).OfType<Button>().Single(x => x.Text == "R&ename voice...");
                var gate = new TaskCompletionSource<int>();
                var work = (Task)typeof(CatalogForm).GetMethod("Work", flags).Invoke(form, new object[] { new Func<CancellationToken, Task>(token => gate.Task) });
                list.Items.Add(Voice("owned")); list.SelectedIndex = 1;
                Check(!rename.Enabled, "Rename must be unavailable while a request is active.");
                gate.SetResult(0); Wait(work);
                Check(rename.Enabled, "Rename must become available after loading an owned voice.");
                var mnemonics = All(form).Where(x => x is Label || x is ButtonBase).Select(x => x.Text).Where(x => x.Contains("&")).Select(x => char.ToUpperInvariant(x[x.IndexOf('&') + 1])).ToList();
                Check(mnemonics.Distinct().Count() == mnemonics.Count, "Voice-library mnemonics must be unique.");
            }
            return checks;
        }
        private static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    }
}
