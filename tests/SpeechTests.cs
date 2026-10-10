using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace ElevenLabsSpeechGenerator
{
    internal static class SpeechTests
    {
        private static int count;
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        private static void Reject(Action action, string message) { try { action(); } catch (InvalidDataException) { count++; return; } throw new Exception(message); }
        [STAThread]
        public static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            using (var context = new System.Windows.Forms.ApplicationContext())
            {
                EventHandler run = null;
                run = delegate
                {
                    System.Windows.Forms.Application.Idle -= run;
                    try { RunChecks(); }
                    catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); Environment.ExitCode = 1; }
                    finally { context.ExitThread(); }
                };
                System.Windows.Forms.Application.Idle += run;
                System.Windows.Forms.Application.Run(context);
            }
        }
        private static void TestNativePlayback()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Native playback test");
            Directory.CreateDirectory(folder);
            var paths = new[] { "one.wav", "two.wav", "three.wav" }.Select(name => Path.Combine(folder, name)).ToArray();
            try
            {
                foreach (var path in paths) using (var wave = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 1))) wave.Write(new byte[8820], 0, 8820);
                var starts = new List<string>(); var errors = new List<Exception>();
                using (var player = new Playback())
                {
                    player.Started += path => starts.Add(path); player.Failed += ex => errors.Add(ex);
                    player.PlaySequence(paths, -1);
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (player.IsPlaying && wait.ElapsedMilliseconds < 6000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(errors.Count == 0 && !player.IsPlaying && starts.SequenceEqual(paths), "Native WAV completion did not play the complete sequence: " + string.Join("; ", errors.Select(x => x.Message)));
                    foreach (var path in paths) using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    player.PlaySequence(paths, -1); player.Stop();
                    int stopped = starts.Count;
                    for (int i = 0; i < 30; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(!player.IsPlaying && starts.Count == stopped, "Native stop allowed another queued file to start.");
                }
            }
            finally { Directory.Delete(folder, true); }
        }
        private static void TestPlaybackQueue()
        {
            var played = new List<string>(); int errors = 0;
            Playback.TestPlay = (path, device) => { Check(device == 2, "Queue lost selected device"); played.Add(path); };
            try
            {
                using (var player = new Playback())
                {
                    player.Failed += ex => errors++;
                    player.PlaySequence(new[] { "first.wav", "second.mp3", "third.wav" }, 2);
                    Check(played.SequenceEqual(new[] { "first.wav" }), "Queue overlapped audio");
                    int first = player.TestGeneration;
                    player.CompleteForTest(first);
                    Check(played.SequenceEqual(new[] { "first.wav", "second.mp3" }), "Queue did not advance");
                    player.Stop(); player.CompleteForTest(first);
                    Check(played.Count == 2, "Stop allowed queued audio to restart");
                    player.Play("manual.wav", 2); player.CompleteForTest(first);
                    Check(played.Last() == "manual.wav", "Stale completion replaced manual play");
                    player.CompleteForTest(player.TestGeneration);
                    Check(!player.IsPlaying, "Completed playback retained a queue");
                    Playback.TestPlay = (path, device) => { throw new IOException("test audio failure"); };
                    player.PlaySequence(new[] { "bad.wav", "never.wav" }, 2);
                    Check(errors == 1 && !player.IsPlaying, "Playback error did not end queue");
                }
            }
            finally { Playback.TestPlay = null; Playback.TestStop = null; }
        }
        private static void TestSpeechTags()
        {
            Check(SpeechTags.Normalize(" [Whispers] ") == "Whispers", "Tag brackets were not normalized");
            Reject(() => SpeechTags.Normalize("[one][two]"), "Nested tags accepted");
            Reject(() => SpeechTags.Normalize("two\nlines"), "Multiline tag accepted");
            Reject(() => SpeechTags.Normalize("[]"), "Empty tag accepted");
            SpeechTags.Save(new[] { "my direction", "MY DIRECTION" });
            Check(SpeechTags.Load().SequenceEqual(new[] { "my direction" }), "Custom tags did not round trip or deduplicate");
            Check(ContextHelp.Description(new System.Windows.Forms.NumericUpDown { AccessibleName = "Stability percent" }).Contains("emotional variation"), "Voice settings need explanatory context help");
            Check(ContextHelp.Description(new System.Windows.Forms.TextBox { AccessibleName = "API key" }).Contains("private credential"), "Preferences must explain why an API key is needed");
            using (var text = new System.Windows.Forms.TextBox { Multiline = true, Text = "Hello world" })
            {
                text.Select(6, 5); SpeechTags.Insert(text, "whispers", 100);
                Check(text.Text == "Hello [whispers] world", "Tag replaced selected passage instead of inserting before it");
                var before = text.Text; Reject(() => SpeechTags.Insert(text, "laughs", before.Length), "Tag exceeded text limit");
                Check(text.Text == before, "Rejected tag modified text");
            }
            Exception failure = null;
            using (var owner = new System.Windows.Forms.Form())
            using (var target = new System.Windows.Forms.TextBox { Text = "Keep this passage" })
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            using (var guard = new System.Windows.Forms.Timer { Interval = 3000 })
            {
                owner.Controls.Add(target); owner.Show(); target.Select(5, 12);
                timer.Tick += delegate
                {
                    var form = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Insert speech tag");
                    if (form == null) return;
                    timer.Stop(); guard.Stop();
                    try
                    {
                        Check(form.Owner == owner && !form.ShowInTaskbar, "Tag picker must remain an owned dialog");
                        CheckHelpCoverage(form);
                        var controls = Descendants(form).ToArray();
                        var keys = controls.Where(x => x.Text.Contains("&") && (x is System.Windows.Forms.Button || x is System.Windows.Forms.Label)).Select(x => char.ToLowerInvariant(x.Text[x.Text.IndexOf('&') + 1])).ToArray();
                        Check(keys.Distinct().Count() == keys.Length, "Tag picker mnemonics collide");
                        var custom = controls.OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Custom tag");
                        custom.Text = "my test delivery";
                        controls.OfType<System.Windows.Forms.Button>().First(x => x.Text == "Sa&ve custom tag").PerformClick();
                        Check(SpeechTags.Load().Contains("my test delivery"), "Picker did not save the custom tag");
                        controls.OfType<System.Windows.Forms.Button>().First(x => x.Text == "&Insert").PerformClick();
                    }
                    catch (Exception ex) { failure = ex; form.Close(); }
                };
                guard.Tick += delegate { guard.Stop(); failure = new Exception("Tag picker timed out"); foreach (var form in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Where(x => x.Text == "Insert speech tag").ToArray()) form.Close(); };
                timer.Start(); guard.Start(); SpeechTags.Show(owner, target, 100);
                if (failure != null) throw failure;
                Check(target.Text == "Keep [my test delivery] this passage" && target.Focused, "Picker did not preserve selection text or restore focus");
                owner.Close();
            }
        }
        private static void TestCloneDialog()
        {
            Exception failure = null;
            using (var owner = new System.Windows.Forms.Form())
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                owner.Show();
                timer.Tick += delegate
                {
                    var form = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Clone voice");
                    if (form == null) return;
                    timer.Stop();
                    try
                    {
                        var controls = Descendants(form).ToArray();
                        var keys = controls.Where(x => x.Text.Contains("&") && (x is System.Windows.Forms.Button || x is System.Windows.Forms.Label || x is System.Windows.Forms.CheckBox)).Select(x => char.ToLowerInvariant(x.Text[x.Text.IndexOf('&') + 1])).ToArray();
                        Check(keys.Distinct().Count() == keys.Length, "Clone dialog mnemonics collide");
                        Check(controls.OfType<System.Windows.Forms.TextBox>().Any(x => x.AccessibleName == "Clone status" && x.ReadOnly && x.TabStop), "Clone upload needs a readable status control");
                        Check(controls.OfType<System.Windows.Forms.Button>().Any(x => x.Text == "Cancel &upload"), "Clone upload needs a separate cancel command");
                        CheckHelpCoverage(form);
                        var add = controls.OfType<System.Windows.Forms.Button>().Single(x => x.Text == "&Add audio files...");
                        var remove = controls.OfType<System.Windows.Forms.Button>().Single(x => x.Text == "&Remove sample");
                        var consent = controls.OfType<System.Windows.Forms.CheckBox>().Single();
                        Check(ContextHelp.Description(add).Contains("recordings") && ContextHelp.Description(remove).Contains("without deleting"), "Clone Add and Remove must explain their different effects");
                        Check(ContextHelp.Description(consent).Contains("speaker") && ContextHelp.Description(consent) != ContextHelp.Description(add), "Clone consent must explain permission, not sample selection");
                        Check(ContextHelp.Description(controls.Single(x => x.AccessibleName == "Voice description")).Contains("Optional"), "Clone description must not explain voice design");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { form.Close(); }
                };
                timer.Start(); ToolDialogs.Clone(owner, new SpeechClient("test", "http://127.0.0.1:1"));
                owner.Close();
            }
            if (failure != null) throw failure;
        }
        private static void RunChecks()
        {
            TestStartupArguments();
            TestCloneDialog();
            count += LongSpeechTests.Run();
            TestSpeechTags();
            var owned = new NamedItem { Id = "owned", Name = "Owned", Data = new Dictionary<string, object> { { "category", "professional" }, { "is_owner", true } } };
            var sharedVoice = new NamedItem { Id = "shared", Name = "Shared", Data = new Dictionary<string, object> { { "category", "professional" }, { "is_owner", false } } };
            Check(VoiceGroups.Matches(owned, "Your voices") && !VoiceGroups.Matches(sharedVoice, "Your voices"), "Voice ownership filtering is incorrect");
            Check(VoiceGroups.Matches(owned, "Cloned voices") && VoiceGroups.Matches(sharedVoice, "Shared voices"), "Voice category filtering is incorrect");
            var audioSettings = new AppSettings { AutoPlayGenerations = true, PlaybackDevice = 2, DefaultOutputFormat = "pcm_24000" };
            audioSettings.Save(); var loadedAudio = AppSettings.Load();
            Check(loadedAudio.AutoPlayGenerations && loadedAudio.PlaybackDevice == 2, "Audio preferences did not round trip");
            Check(loadedAudio.DefaultOutputFormat == "pcm_24000", "Default audio format did not round trip");
            using (var prefs = new PreferencesForm(audioSettings, 3))
            {
                var tabs = Descendants(prefs).OfType<System.Windows.Forms.TabControl>().Single();
                Check(tabs.SelectedTab.Text == "Audio", "Audio command must open the shared Audio preferences tab");
                Check(Descendants(tabs.SelectedTab).OfType<System.Windows.Forms.ComboBox>().Any(x => x.AccessibleName == "Default output format"), "Audio tab must contain default format");
            }
            var root = Path.Combine(AppPaths.AppFolder, "Output grouping");
            Check(FileNames.GenerationFolder(root, SpeechMode.TextToSpeech, "Adam - Warm") == Path.Combine(root, "Adam - Warm"), "Single voice output must be grouped by voice");
            Check(FileNames.GenerationFolder(root, SpeechMode.Dialogue, "Adam") == Path.Combine(root, "Dialogue"), "Dialogue output must not claim a single voice");
            Check(FileNames.GenerationFolder(root, SpeechMode.VoiceDesign, "") == Path.Combine(root, "Voice design"), "Voice design output must have a stable folder");
            Check(FileNames.GenerationFolder(root, SpeechMode.Transcription, "") == root, "Transcription location must remain unchanged");
            Check(Path.GetDirectoryName(FileNames.GenerationFolder(root, SpeechMode.TextToSpeech, "../../escape")) == root, "Voice names must not escape the output folder");
            Check(VoicePreview.Url(new NamedItem { Data = new Dictionary<string, object> { { "preview_url", "https://example.invalid/voice.mp3" } } }) != null, "HTTPS preview missing");
            Check(VoicePreview.Url(new NamedItem { Data = new Dictionary<string, object> { { "preview_url", "http://example.invalid/voice.mp3" } } }) == null && VoicePreview.Url(null) == null, "Unavailable or insecure preview must be disabled");
            Check(!new AppSettings().AutoPlayGenerations, "Autoplay must be opt-in");
            TestPlaybackQueue();
            TestNativePlayback();
            var p = new SpeechProject { VoiceId = "voice", Text = "Hello" };
            p.Validate(10000);
            var model = new NamedItem { Data = new Dictionary<string, object> { { "can_use_style", false }, { "can_use_speaker_boost", false } } };
            Check(!p.VoiceSettings(model).ContainsKey("style"), "Unsupported style must not be submitted");
            Check(!p.VoiceSettings(model).ContainsKey("use_speaker_boost"), "Unsupported boost must not be submitted");
            p.Speed = double.NaN; Reject(() => p.Validate(10000), "NaN setting accepted"); p.Speed = 1;
            model.Data["can_use_style"] = true; model.Data["can_use_speaker_boost"] = true;
            Check(!p.VoiceSettings(model).ContainsKey("speed") && !p.VoiceSettings(model).ContainsKey("style") && !p.VoiceSettings(model).ContainsKey("use_speaker_boost"), "V4 accepts stability and similarity only");
            Check(!Enum.GetNames(typeof(SpeechMode)).Contains("SoundEffects"), "Sound effects must remain in the Music companion");
            p.Mode = (SpeechMode)4; Reject(() => p.ValidateStructure(), "Removed sound-effects mode must not become voice design");
            p.Mode = SpeechMode.Dialogue; p.Dialogue.Add(new DialogueLine { VoiceId = "voice", VoiceName = "voice", Text = new string('x', 2001) });
            Reject(() => p.Validate(10000), "Dialogue limit ignored");
            p.Dialogue[0].Text = "hello";
            Check(p.JsonBody(model).ContainsKey("settings") && !p.JsonBody(model).ContainsKey("voice_settings"), "Dialogue uses different schema");
            p.Dictionaries = null; Reject(() => p.Validate(10000), "Null dictionary collection accepted");
            Check(FileNames.Stem("CON") == "_CON", "Reserved device name");
            Check(FileNames.Stem("My speech") == "My speech", "Spaces changed");
            Check(SpeechProject.WindowsLines("one\ntwo\rthree\r\nfour") == "one\r\ntwo\r\nthree\r\nfour", "Native line breaks");
            Check(JsonData.Items(JsonData.Object("{\"voices\":[{\"voice_id\":\"fixture\"}]}"), "voices").Count() == 1, "Typed JSON collections must retain voice entries");
            Check(SpeechProject.TextLength("\ud83d\ude00") == 2, "Conservative Unicode count matches editor boundary");
            var encoded = JsonData.Encode(new SpeechProject { Text = "one\ntwo" });
            Check(JsonData.Serializer().Deserialize<SpeechProject>(encoded).Text == "one\ntwo", "Project newline roundtrip");
            const string disposableKey = "test-key-not-a-real-credential";
            var musicFormat = Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(disposableKey), System.Text.Encoding.UTF8.GetBytes("ElevenLabsMusicGenerator.ApiKey.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser));
            Check(ApiKeyProtector.Unprotect(musicFormat) == disposableKey, "Music key file must load in Speech");
            var speechFormat = ApiKeyProtector.Protect(disposableKey);
            Check(System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(speechFormat), System.Text.Encoding.UTF8.GetBytes("ElevenLabsMusicGenerator.ApiKey.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser)) == disposableKey, "Speech key file must load in Music");
            AppPaths.SaveApiKey(disposableKey);
            File.WriteAllText(Path.Combine(AppPaths.UserFolder, "ApiKey.dat"), musicFormat);
            AppPaths.SaveApiKey("");
            Check(AppPaths.LoadApiKey() == "", "Clearing a key must not resurrect the copied fallback");
            Check(SpeechClient.PreviewExtension(new Dictionary<string, object> { { "media_type", "audio/wav" } }) == ".wav", "WAV previews must have WAV filenames");
            TestDialogueInitialization();
            TestModeDefaults();
            TestMenuShortcutDescriptions();
            TestVoiceListPrefixNavigation();
            TestPlaybackShortcuts();
            TestBusyFocusRestoration();
            TestResultAccessibility();
            TestQuotedOutputFolder();
            TestQuotedPreferencesSave();
            TestContextHelp();
            var reusable = SpeechProject.FromJson(JsonData.Encode(new { project = new SpeechProject { Text = "Reused" }, request_id = "fixture" }));
            Check(reusable.Text == "Reused", "Generation details must reopen as a speech project");
            Reject(() => SpeechProject.FromJson("{\"text\":\"Transcript\"}"), "Transcript details mistaken for a project");
            var malformed = new Dictionary<string, object> { { "words", new object[] {
                new Dictionary<string, object> { { "start", double.NaN }, { "end", double.PositiveInfinity }, { "text", "Invalid" } },
                new Dictionary<string, object> { { "start", 1e30 }, { "end", 1e30 }, { "text", "Overflow" } },
                new Dictionary<string, object> { { "start", 1.0 }, { "end", 2.0 }, { "text", "Valid" } }
            } } };
            Check(Transcript.Subtitles(malformed) == "1\r\n00:00:01,000 --> 00:00:02,000\r\nValid\r\n\r\n", "Malformed timestamps must not interrupt valid subtitle export");
            count += ApiTests.Run();
            Console.WriteLine("Passed " + count + " speech-domain checks.");
        }

        private static void TestStartupArguments()
        {
            var parser = typeof(Program).GetMethod("InitialDocument", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Func<string[], string> parse = args => (string)parser.Invoke(null, new object[] { args });
            Check(parse(new[] { "--cleanup-update", "update staging" }) == null, "Updater cleanup folder was interpreted as a project");
            Check(parse(new[] { "--cleanup-update", "update staging", "project.speech.json" }) == "project.speech.json", "Cleanup consumed a real project argument");
            Check(parse(new[] { "--CLEANUP-UPDATE", "update staging" }) == null, "Cleanup option must be case insensitive");
            Check(parse(new string[0]) == null && parse(new[] { "project.speech.json" }) == "project.speech.json", "Normal document startup changed");
        }
        private static void TestContextHelp()
        {
            using (var preferences = new PreferencesForm(new AppSettings())) CheckHelpCoverage(preferences);
            using (var main = new MainForm(null)) CheckHelpCoverage(main);
            foreach (var kind in new[] { "Voices", "History", "Dictionaries" })
                using (var playback = new Playback())
                using (var catalog = new CatalogForm(kind, new SpeechClient("test", "http://127.0.0.1:1"), new AppSettings(), playback, new SpeechProject()))
                    CheckHelpCoverage(catalog);
            using (var main = new MainForm(null))
            using (var owner = Ui.Dialog("Preferences", new System.Drawing.Size(600, 400)))
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            using (var guard = new System.Windows.Forms.Timer { Interval = 2000 })
            {
                main.Location = owner.Location = new System.Drawing.Point(-2000, -2000);
                main.Show(); owner.Owner = main;
                var key = Ui.Text("API key", false); key.UseSystemPasswordChar = true; key.Text = "secret-test-value-must-not-appear"; owner.Controls.Add(key); owner.Show(); key.Focus();
                Exception failure = null;
                timer.Tick += delegate
                {
                    var dialog = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Help: API key");
                    if (dialog == null) return;
                    timer.Stop(); guard.Stop();
                    try
                    {
                        var text = Descendants(dialog).OfType<System.Windows.Forms.TextBox>().Single();
                        Check(text.ReadOnly && text.Multiline && text.TabStop && !dialog.ShowInTaskbar, "Context help must be readable and owned, not another taskbar item.");
                        Check(string.IsNullOrEmpty(text.AccessibleDescription), "Help must not repeat basic screen-reader navigation instructions.");
                        Check(text.Text.Contains("ElevenLabs key") && !text.Text.Contains(key.Text), "Context help must explain API keys without exposing their values.");
                        Check(dialog.Owner == owner, "Help must belong to the current dialog.");
                    }
                    catch (Exception ex) { failure = ex; }
                    dialog.Close();
                };
                guard.Tick += delegate { guard.Stop(); foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Where(x => x.Text.StartsWith("Help: ")).ToArray()) f.Close(); failure = new Exception("Context-help capture timed out."); };
                timer.Start(); guard.Start();
                var message = System.Windows.Forms.Message.Create(key.Handle, 0x0100, (IntPtr)(int)System.Windows.Forms.Keys.F1, IntPtr.Zero);
                Check(System.Windows.Forms.Application.FilterMessage(ref message), "F1 must work inside an owned Preferences dialog.");
                if (failure != null) throw failure;
                Check(key.Focused, "Closing help must restore its original focused control.");
                owner.Close(); main.Close();
            }
        }
        private static void CheckHelpCoverage(System.Windows.Forms.Control container)
        {
            foreach (var control in Descendants(container).Where(x => x is System.Windows.Forms.ButtonBase || x is System.Windows.Forms.TextBox || x is System.Windows.Forms.ComboBox || x is System.Windows.Forms.ListBox || x is System.Windows.Forms.NumericUpDown || x is System.Windows.Forms.LinkLabel || x is System.Windows.Forms.TabControl))
            {
                var help = ContextHelp.Description(control);
                Check(!help.Contains("additional description") && !help.Contains("full workflow") && !help.Contains("Use this control"), "Missing specific context help: " + container.Text + " / " + ContextHelp.ControlName(control));
            }
        }
        private static void TestResultAccessibility()
        {
            using (var form = new ApiKeyTestResultForm("Choose a valid output folder.", "Could not use output folder"))
            {
                Check(form.AccessibleName == "Could not use output folder", "Folder errors must not announce an API-key dialog name.");
                var edit = Descendants(form).OfType<System.Windows.Forms.TextBox>().Single();
                Check(edit.AccessibleName == form.AccessibleName && edit.ReadOnly && edit.Multiline && edit.TabStop, "Result text must have the actual dialog name and remain readable.");
            }
        }

        private static void TestPlaybackShortcuts()
        {
            AppPaths.SaveApiKey("");
            new AppSettings { UpdateCheckFrequency = "Never" }.Save();
            int plays = 0, stops = 0;
            Playback.TestPlay = (path, device) => { Check(path == "Selected.wav", "Enter played the wrong result."); plays++; };
            Playback.TestStop = () => stops++;
            try
            {
                using (var form = new MainForm(null))
                {
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-2000, -2000);
                    form.Show();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    typeof(MainForm).GetMethod("Bind", flags).Invoke(form, null);
                    var method = typeof(MainForm).GetMethod("ProcessCmdKey", flags);
                    var list = Descendants(form).OfType<System.Windows.Forms.ListBox>().First(x => x.AccessibleName == "Generated files and voice previews");
                    list.Items.Add(new NamedItem { Id = "Selected.wav", Name = "Selected.wav" }); list.SelectedIndex = 0; list.Focus();
                    var msg = new System.Windows.Forms.Message();
                    Check(list.Focused, "The generated-files list must accept focus.");
                    Check((bool)method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Enter }) && plays == 1, "Enter in the results list must play once without generating audio.");
                    int beforeStop = stops;
                    Check((bool)method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Escape }) && stops == beforeStop + 1 && form.Visible, "Escape in the list must stop playback, not close the app.");
                    list.ClearSelected(); method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Enter });
                    Check(plays == 1, "Enter with no selected output must not generate or play audio.");
                    var prompt = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Speech text"); prompt.Focus();
                    Check(!(bool)method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Enter }) && plays == 1, "Enter in speech text must remain a normal editing key.");
                    var voice = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Voice");
                    var model = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Model");
                    var voiceSettings = Descendants(form).OfType<System.Windows.Forms.Button>().First(x => x.Text.Replace("&", "") == "Voice settings...");
                    var status = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Status log");
                    var group = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Voice group");
                    Check(form.SelectNextControl(prompt, true, true, true, true) && group.Focused, "Tab from prompt must reach Voice group.");
                    Check(form.SelectNextControl(group, true, true, true, true) && voice.Focused, "Tab from Voice group must reach Voice.");
                    Check(form.SelectNextControl(voice, true, true, true, true) && model.Focused, "Tab from Voice must reach Model.");
                    Check(form.SelectNextControl(model, true, true, true, true) && voiceSettings.Focused, "Tab from Model must reach Voice settings.");
                    prompt.Focus();
                    Check(form.SelectNextControl(prompt, false, true, true, true) && status.Focused, "Shift+Tab from the prompt must retain quick access to Status log.");
                    foreach (var size in new[] { new System.Drawing.Size(800, 600), new System.Drawing.Size(1040, 780) })
                    {
                        form.Size = size; form.PerformLayout();
                        Check(voice.Parent.PointToScreen(System.Drawing.Point.Empty).Y >= prompt.PointToScreen(new System.Drawing.Point(0, prompt.Height)).Y, "Voice controls must sit below the prompt at " + size);
                        Check(voice.Parent.Width >= voice.Right && model.Parent.Width >= model.Right && voiceSettings.Parent.Width >= voiceSettings.Right, "Voice controls must fit at minimum and normal window sizes.");
                        Check(prompt.Height >= 100, "The prompt must remain usable in a small window.");
                    }
                    var mode = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Mode");
                    for (int i = 0; i < mode.Items.Count; i++)
                    {
                        mode.SelectedIndex = i;
                        Check(voiceSettings.Visible == (i == 0 || i == 1 || i == 3), "Voice settings visibility must match the active mode.");
                        var dialogueCommand = form.MainMenuStrip.Items.Cast<System.Windows.Forms.ToolStripMenuItem>().SelectMany(x => x.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>()).First(x => x.ShortcutKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P));
                        Check(dialogueCommand.Enabled == (i == 1), "Dialogue editing must be enabled only in Dialogue mode.");
                        var keys = Descendants(form).Where(x => x.Visible && (x is System.Windows.Forms.Button || x is System.Windows.Forms.Label || x is System.Windows.Forms.CheckBox)).Select(x => x.Text).Where(x => x.Contains("&")).Select(x => char.ToLowerInvariant(x[x.IndexOf('&') + 1])).Concat(new[] { 'f', 'a', 't', 'h', 'm', 's', 'b' }).ToArray();
                        Check(keys.Distinct().Count() == keys.Length, "Main-window mnemonics must not clash in mode " + mode.Text);
                    }
                    mode.SelectedIndex = 0;
                    prompt.Text = "Keep this speech draft.";
                    method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P });
                    Check(mode.SelectedIndex == 0 && prompt.Text == "Keep this speech draft.", "Ctrl+P outside Dialogue must not open an editor or change drafts.");
                    using (var cancellation = new System.Threading.CancellationTokenSource())
                    {
                        var operation = typeof(MainForm).GetField("operation", flags); operation.SetValue(form, cancellation);
                        beforeStop = stops;
                        method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Escape });
                        Check(cancellation.IsCancellationRequested && stops == beforeStop + 1, "Escape during generation must cancel the request and stop playback.");
                        operation.SetValue(form, null);
                    }
                    var file = (System.Windows.Forms.ToolStripMenuItem)form.MainMenuStrip.Items[0]; file.ShowDropDown();
                    beforeStop = stops;
                    method.Invoke(form, new object[] { msg, System.Windows.Forms.Keys.Escape });
                    Check(stops == beforeStop, "Escape must not trigger playback actions while a menu is open.");
                    file.HideDropDown();
                    form.Close();
                }
            }
            finally { Playback.TestPlay = null; Playback.TestStop = null; }
        }

        private static void TestBusyFocusRestoration()
        {
            AppPaths.SaveApiKey("");
            new AppSettings { UpdateCheckFrequency = "Never" }.Save();
            using (var form = new MainForm(null))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(MainForm).GetMethod("Bind", flags).Invoke(form, null);
                var voice = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Voice");
                var status = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Status log");
                var filename = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Base filename");
                var preview = Descendants(form).OfType<System.Windows.Forms.Button>().First(x => x.AccessibleName == "Preview voice");
                var voices = (List<NamedItem>)typeof(MainForm).GetField("voices", flags).GetValue(form);
                voices.Add(new NamedItem { Id = "test-voice", Name = "Test voice", Data = new Dictionary<string, object> { { "preview_url", "https://example.invalid/preview.mp3" } } });
                ((SpeechProject)typeof(MainForm).GetField("project", flags).GetValue(form)).VoiceId = "test-voice";
                typeof(MainForm).GetMethod("Bind", flags).Invoke(form, null);
                foreach (var origin in new System.Windows.Forms.Control[] { voice, preview })
                foreach (var scenario in new[] { "complete", "cancel", "error", "read status", "return to filename", "background" })
                {
                    using (var other = new System.Windows.Forms.Form { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000) })
                    using (var dismissError = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                    Check(origin.Focus(), "The preview origin must accept focus.");
                    var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> work = token => gate.Task;
                    var task = (System.Threading.Tasks.Task)typeof(MainForm).GetMethod("Run", flags).Invoke(form, new object[] { "Preview focus test", work });
                    Check(!voice.Enabled && !voice.Focused, "The test must reproduce busy-state focus displacement.");
                    if (scenario == "read status" || scenario == "return to filename") status.Focus();
                    if (scenario == "return to filename") filename.Focus();
                    var otherEdit = new System.Windows.Forms.TextBox(); other.Controls.Add(otherEdit);
                    if (scenario == "background") { other.Show(); otherEdit.Focus(); Check(!form.ContainsFocus && otherEdit.Focused, "The test must move focus to a different window."); }
                    dismissError.Tick += delegate
                    {
                        var result = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Preview focus test failed");
                        if (result != null) result.Close();
                    };
                    dismissError.Start();
                    if (scenario == "cancel") gate.SetCanceled();
                    else if (scenario == "error") gate.SetException(new IOException("Disposable preview download failure."));
                    else gate.SetResult(true);
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (!task.IsCompleted && wait.ElapsedMilliseconds < 3000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    Check(task.IsCompleted && !task.IsFaulted, "The preview operation did not finish.");
                    Check(scenario == "background" ? otherEdit.Focused : scenario == "read status" ? status.Focused : scenario == "return to filename" ? filename.Focused : origin.Focused,
                        scenario == "read status" || scenario == "return to filename" ? "Finishing a preview must respect deliberate focus changes." : "Finishing or cancelling preview must restore its original control, not leave focus on the filename.");
                    dismissError.Stop(); other.Close(); form.Activate();
                    }
                }
                form.Close();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private static void TestVoiceListPrefixNavigation()
        {
            const int CharacterMessage = 0x0102;
            const int KeyDownMessage = 0x0100;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            using (var player = new Playback())
            using (var form = new CatalogForm("Voices", new SpeechClient(""), new AppSettings(), player, new SpeechProject()))
            using (var host = new System.Windows.Forms.Form { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000) })
            {
                var list = (System.Windows.Forms.ListBox)typeof(CatalogForm).GetField("list", flags).GetValue(form);
                host.Controls.Add(list); host.Show(); list.Focus();
                foreach (var name in new[] { "Sarah", "Stephen", "Steve", "Susan", "Technology" }) list.Items.Add(new NamedItem { Name = name });
                Action<string> type = text => { foreach (char value in text) SendMessage(list.Handle, CharacterMessage, new IntPtr(value), IntPtr.Zero); };
                Action<System.Windows.Forms.Keys> key = value => SendMessage(list.Handle, KeyDownMessage, new IntPtr((int)value), IntPtr.Zero);
                list.SelectedIndex = 4; type("s");
                Check(list.SelectedIndex == 0, "First-letter navigation must select Sarah, actual index: " + list.SelectedIndex);
                type("t");
                Check(list.SelectedIndex == 1, "Typing St must select Stephen, not jump to Technology.");
                type("e");
                Check(list.SelectedIndex == 1, "Extending the prefix must retain the current match.");
                type("v");
                Check(list.SelectedIndex == 2, "Typing Stev must narrow the selection to Steve.");
                type("z");
                Check(list.SelectedIndex == 2, "An unmatched prefix must not jump to an unrelated letter.");
                key(System.Windows.Forms.Keys.Home); type("s");
                Check(list.SelectedIndex == 1, "Navigation keys must reset the prefix and search from the selection.");
                type("s");
                Check(list.SelectedIndex == 2, "Repeated single letters must cycle matches.");
                type("s");
                Check(list.SelectedIndex == 3, "Repeated letters must continue cycling.");
                type("s");
                Check(list.SelectedIndex == 0, "Single-letter cycling must wrap around.");
                type("t"); key(System.Windows.Forms.Keys.Back); type("u");
                Check(list.SelectedIndex == 3, "Backspace must let the user correct St to Su.");
                System.Threading.Thread.Sleep(1100); type("t");
                Check(list.SelectedIndex == 4, "A pause must start a fresh prefix.");
                typeof(System.Windows.Forms.Control).GetMethod("OnLeave", flags).Invoke(list, new object[] { EventArgs.Empty }); type("s");
                Check(list.SelectedIndex == 0, "Leaving the list must reset its prefix.");
                list.Items.Clear(); type("x");
                Check(list.SelectedIndex == -1, "Typing in an empty voice list must be harmless.");
                Check(list.AccessibilityObject.Role == System.Windows.Forms.AccessibleRole.List, "Prefix navigation must preserve the native accessible list role.");
            }
        }

        private static void TestMenuShortcutDescriptions()
        {
            using (var form = new MainForm(null))
            {
                var items = form.MainMenuStrip.Items.Cast<System.Windows.Forms.ToolStripMenuItem>().SelectMany(x => x.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>()).OfType<System.Windows.Forms.ToolStripMenuItem>();
                foreach (var item in items.Where(x => x.ShortcutKeys != System.Windows.Forms.Keys.None))
                    Check(!string.IsNullOrEmpty(item.ShortcutKeyDisplayString) && item.AccessibleDescription == item.ShortcutKeyDisplayString, "Menus must expose and display their shortcut: " + item.Text);
                var save = items.First(x => x.ShortcutKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S));
                Check(save.ShortcutKeyDisplayString == "Ctrl+S", "Save Project must show Ctrl+S like Music.");
                var clone = items.First(x => x.Text.Replace("&", "") == "Clone voice...");
                Check(clone.ShortcutKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.N), "Clone Voice must use Ctrl+Shift+N, leaving Ctrl+N for New Project.");
                Check(clone.ShortcutKeyDisplayString == "Ctrl+Shift+N" && clone.AccessibleDescription == "Ctrl+Shift+N", "Clone Voice must display and expose its shortcut.");
                Check(items.Where(x => x.ShortcutKeys != System.Windows.Forms.Keys.None).GroupBy(x => x.ShortcutKeys).All(x => x.Count() == 1), "Menu shortcuts must not conflict.");
                var stop = Descendants(form).OfType<System.Windows.Forms.Button>().First(x => x.Text == "Stop");
                Check(stop.AccessibilityObject.KeyboardShortcut == "Esc", "Stop must expose Escape as its shortcut.");
                var voiceSettings = Descendants(form).OfType<System.Windows.Forms.Button>().First(x => x.Text.Replace("&", "") == "Voice settings...");
                Check(voiceSettings.Text == "Vo&ice settings...", "Voice settings must have the available Alt+I mnemonic.");
                Check(string.Equals(voiceSettings.AccessibilityObject.KeyboardShortcut, "Alt+I", StringComparison.OrdinalIgnoreCase), "Voice settings must expose Alt+I to screen readers.");
                var saveCopy = Descendants(form).OfType<System.Windows.Forms.Button>().FirstOrDefault(x => x.Text == "Save a cop&y...");
                Check(saveCopy != null && string.Equals(saveCopy.AccessibilityObject.KeyboardShortcut, "Alt+Y", StringComparison.OrdinalIgnoreCase), "Saving an extra copy must expose Alt+Y and explain that it is a copy.");
                var prompt = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Speech text");
                prompt.Text = "Count this";
                Check(form.AccessibleName == form.Text && form.Text.Contains("10 /"), "NVDA's window name must include the current character count.");
                var dialogue = items.First(x => x.ShortcutKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P));
                Check(!dialogue.Enabled, "Dialogue editing must be unavailable outside Dialogue mode.");
                foreach (var key in new[] { System.Windows.Forms.Keys.L, System.Windows.Forms.Keys.U, System.Windows.Forms.Keys.H, System.Windows.Forms.Keys.D })
                    Check(items.Any(x => x.ShortcutKeys == (System.Windows.Forms.Keys.Control | key)), "Useful Tools shortcut is missing: Ctrl+" + key);
            }
        }

        private static void TestQuotedPreferencesSave()
        {
            var expected = Path.Combine(AppPaths.AppFolder, "Quoted preferences");
            using (var form = new PreferencesForm(new AppSettings()))
            using (var action = new System.Windows.Forms.Timer { Interval = 50 })
            using (var guard = new System.Windows.Forms.Timer { Interval = 1500 })
            {
                Exception failure = null;
                var started = false;
                action.Tick += delegate
                {
                    if (started) return;
                    started = true; action.Stop();
                    Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Default output folder").Text = "  \"" + expected + "\"  ";
                    ((System.Windows.Forms.Button)form.AcceptButton).PerformClick();
                };
                guard.Tick += delegate
                {
                    guard.Stop(); failure = new Exception("Preference save did not close directly; an unexpected dialog or validation prevented saving.");
                    foreach (var other in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Where(x => x != form).ToArray()) other.Close();
                    form.Close();
                };
                action.Start(); guard.Start();
                var result = form.ShowDialog();
                guard.Stop();
                if (failure != null) throw failure;
                Check(result == System.Windows.Forms.DialogResult.OK && AppSettings.Load().DefaultOutputFolder == expected, "Quoted preferences must save silently with the correct folder.");
            }
            Directory.Delete(expected);
        }

        private static void TestQuotedOutputFolder()
        {
            var expected = Path.Combine(AppPaths.AppFolder, "Quoted output");
            var ini = new IniFile(); ini.Set("General", "OutputFolder", "  \"" + expected + "\"  ");
            ini.Save(AppPaths.SettingsPath, new string[0]);
            try { Check(AppSettings.Load().DefaultOutputFolder == expected, "Explorer-quoted output folders must load without path errors"); }
            finally { File.Delete(AppPaths.SettingsPath); }
            Check(AppSettings.ResolveOutputFolder("  \".\\Audio\"  ", false) == Path.Combine(AppPaths.AppFolder, "Audio"), "Relative portable paths must remain app-relative.");
            Check(AppSettings.ResolveOutputFolder(expected + "\\", true) == Path.GetFullPath(expected + "\\"), "Valid trailing folder separators must be preserved.");
            Check(AppSettings.ResolveOutputFolder(@"\\server\share\Audio", true) == @"\\server\share\Audio", "UNC folders must remain supported without contacting the server.");
            Environment.SetEnvironmentVariable("ELEVENLABS_TEST_FOLDER", expected);
            try { Check(AppSettings.ResolveOutputFolder("\"%ELEVENLABS_TEST_FOLDER%\"", true) == expected, "Quoted environment-variable paths must expand."); }
            finally { Environment.SetEnvironmentVariable("ELEVENLABS_TEST_FOLDER", null); }
            foreach (var invalid in new[] { "", "relative", @"C:relative", @"\relative", "\"" + expected, expected + "\"", expected + "\\bad\"name" })
            {
                bool rejected = false;
                try { AppSettings.ResolveOutputFolder(invalid, true); } catch (InvalidDataException ex) { rejected = ex.Message.Contains("output folder"); }
                Check(rejected, "Invalid or ambiguous paths must identify the output folder: " + invalid);
            }
        }
        private static void TestDialogueInitialization()
        {
            var p = new SpeechProject { Mode = SpeechMode.Dialogue };
            p.Dialogue.Add(new DialogueLine { VoiceId = "first", VoiceName = "First", Text = "Keep this first line.\nAnd this second line." });
            Exception failure = null;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate
                {
                    var form = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Dialogue");
                    if (form == null) return; timer.Stop();
                    try
                    {
                        var text = Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Text for this line");
                        CheckHelpCoverage(form);
                        Check(text.Text == SpeechProject.WindowsLines(p.Dialogue[0].Text), "Opening dialogue overwrote the first line");
                        var ok = Descendants(form).OfType<System.Windows.Forms.Button>().First(x => x.Text == "&OK"); ok.PerformClick();
                    }
                    catch (Exception ex) { failure = ex; form.Close(); }
                };
                timer.Start(); ToolDialogs.Dialogue(null, p, new List<NamedItem> { new NamedItem { Id = "first", Name = "First" } });
            }
            if (failure != null) throw failure;
            Check(p.Dialogue[0].Text == "Keep this first line.\r\nAnd this second line.", "Dialogue commit lost text");
        }
        private static void TestModeDefaults()
        {
            using (var dubbing = new DubbingForm(new SpeechClient("fixture-not-a-key"), AppPaths.AppFolder, path => { }))
            {
                dubbing.Show(); System.Windows.Forms.Application.DoEvents();
                CheckHelpCoverage(dubbing);
                Check(!dubbing.ShowInTaskbar, "Dubbing must belong to its parent rather than add a taskbar window");
                var controls = Descendants(dubbing).ToArray();
                var status = controls.OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Dubbing status");
                Check(status.ReadOnly && status.Multiline && status.TabStop, "Dubbing progress must be keyboard readable");
                var source = controls.OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Dubbing source");
                var browse = controls.OfType<System.Windows.Forms.Button>().First(x => x.Text == "&Browse...");
                Check(browse.Enabled, "Local dubbing must allow browsing");
                source.SelectedIndex = 1;
                Check(!browse.Enabled, "URL dubbing must not expose local browsing");
                Check(!controls.OfType<System.Windows.Forms.Button>().First(x => x.Text == "Stop &waiting").Enabled, "Idle dubbing must not offer cancellation");
                var mnemonics = controls.Where(x => x.Text.Contains("&")).Select(x => char.ToLowerInvariant(x.Text[x.Text.IndexOf('&') + 1])).ToArray();
                Check(mnemonics.Distinct().Count() == mnemonics.Length, "Dubbing dialog mnemonic collision");
                Check(ContextHelp.Description(status).Contains("without submitting"), "Dubbing help must explain safe resume");
                dubbing.Close();
            }
            using (var form = new MainForm(null))
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(MainForm).GetMethod("Bind", flags).Invoke(form, null);
                var mode = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Mode");
                var model = Descendants(form).OfType<System.Windows.Forms.ComboBox>().First(x => x.AccessibleName == "Model");
                var expected = new[] { "eleven_v4", "eleven_v4", "scribe_v2", "eleven_multilingual_sts_v2", "eleven_ttv_v3", "audio_isolation", "forced_alignment", "eleven_ttv_v3" };
                for (int i = 0; i < expected.Length; i++)
                {
                    mode.SelectedIndex = i;
                    Check(((NamedItem)model.SelectedItem).Id == expected[i], "Fresh mode selected an incompatible model: " + mode.Text);
                }
                foreach (System.Windows.Forms.ToolStripMenuItem menu in form.MainMenuStrip.Items)
                {
                    var keys = menu.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().Select(x => x.Text).Where(x => x.Contains("&")).Select(x => char.ToLowerInvariant(x[x.IndexOf('&') + 1])).ToArray();
                    Check(keys.Distinct().Count() == keys.Length, "Menu mnemonic collision: " + menu.Text);
                }
                var preferences = form.MainMenuStrip.Items.Cast<System.Windows.Forms.ToolStripMenuItem>().SelectMany(x => x.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>()).OfType<System.Windows.Forms.ToolStripMenuItem>().First(x => x.ShortcutKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Oemcomma));
                Check(preferences.ShortcutKeyDisplayString == "Ctrl+,", "Preferences must display Ctrl+, rather than the framework key name");
            }
        }
        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls) { yield return c; foreach (var nested in Descendants(c)) yield return nested; }
        }
    }
}
