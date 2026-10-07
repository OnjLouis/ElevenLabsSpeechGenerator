using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal static class SpeechTags
    {
        public const string GuideUrl = "https://elevenlabs.io/docs/best-practices/prompting";
        private const int MaximumCustomTags = 200;
        public static readonly string[] Examples = { "curious", "crying", "mischievously", "whispers", "shouts", "laughs", "clears throat", "sighs", "excited", "sarcastic", "exhales" };
        private static string PathName { get { return Path.Combine(AppPaths.UserFolder, "SpeechTags.json"); } }
        public static string Normalize(string value)
        {
            var tag = (value ?? "").Trim();
            if (tag.StartsWith("[") && tag.EndsWith("]") && tag.Length > 1) tag = tag.Substring(1, tag.Length - 2).Trim();
            if (tag.Length == 0 || tag.Length > 64 || tag.Any(c => char.IsControl(c) || c == '[' || c == ']')) throw new InvalidDataException("Enter one tag of up to 64 characters, without line breaks or nested brackets.");
            return tag;
        }
        public static List<string> Load()
        {
            if (!File.Exists(PathName)) return new List<string>();
            if (new FileInfo(PathName).Length > 65536) throw new InvalidDataException("The saved tag list is too large.");
            var tags = JsonData.Serializer().Deserialize<string[]>(File.ReadAllText(PathName));
            if (tags == null || tags.Length > MaximumCustomTags) throw new InvalidDataException("The saved tag list is invalid.");
            return tags.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static void Save(IEnumerable<string> values)
        {
            var tags = values.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (tags.Length > MaximumCustomTags) throw new InvalidDataException("Save no more than 200 custom tags.");
            FileNames.AtomicText(PathName, ReadableJson.Format(JsonData.Encode(tags)));
        }
        public static void Insert(TextBoxBase target, string tag, int maximumLength)
        {
            var value = "[" + Normalize(tag) + "] ";
            if (target.TextLength + value.Length > maximumLength) throw new InvalidDataException("There is not enough room for this tag. Shorten the text first.");
            // Inserting before a selection preserves the passage the user intends to direct.
            target.SelectionLength = 0; target.SelectedText = value; target.Focus();
        }
        public static void Show(IWin32Window owner, TextBoxBase target, int maximumLength)
        {
            try
            {
                var customs = Load(); int position = target.SelectionStart;
                using (var form = Ui.Dialog("Insert speech tag", new Size(650, 520)))
                {
                    var layout = Ui.Layout(2);
                    var search = Ui.Text("Search tags", false);
                    var list = new ListBox { AccessibleName = "Speech tags", Dock = DockStyle.Fill, Height = 180 };
                    var custom = Ui.Text("Custom tag", false); custom.MaxLength = 66;
                    var remove = Ui.Button("&Remove custom tag", () => { });
                    Action refresh = () => { var selected = Convert.ToString(list.SelectedItem); list.Items.Clear(); list.Items.AddRange(Examples.Concat(customs).Distinct(StringComparer.OrdinalIgnoreCase).Where(x => x.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x).Cast<object>().ToArray()); if (list.Items.Contains(selected)) list.SelectedItem = selected; else if (list.Items.Count > 0) list.SelectedIndex = 0; };
                    search.TextChanged += delegate { refresh(); };
                    list.SelectedIndexChanged += delegate { custom.Text = Convert.ToString(list.SelectedItem); remove.Enabled = customs.Contains(custom.Text, StringComparer.OrdinalIgnoreCase); };
                    Ui.Row(layout, "&Search tags:", search); Ui.Row(layout, "", list); Ui.Row(layout, "&Tag text:", custom);
                    var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
                    var insert = Ui.Button("&Insert", () => { try { var tag = Normalize(custom.Text); target.Select(position, 0); Insert(target, tag, maximumLength); form.DialogResult = DialogResult.OK; } catch (Exception ex) { Ui.Result(form, "Could not insert tag", ex.Message); } });
                    var save = Ui.Button("Sa&ve custom tag", () => { try { var tag = Normalize(custom.Text); var candidate = customs.Concat(new[] { tag }).ToList(); Save(candidate); customs = candidate.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); search.Clear(); refresh(); custom.Text = tag; } catch (Exception ex) { Ui.Result(form, "Could not save tag", ex.Message); } });
                    remove.Click += delegate { try { var tag = Convert.ToString(list.SelectedItem); var candidate = customs.Where(x => !string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)).ToList(); Save(candidate); customs = candidate; refresh(); } catch (Exception ex) { Ui.Result(form, "Could not remove tag", ex.Message); } };
                    buttons.Controls.Add(insert); buttons.Controls.Add(save); buttons.Controls.Add(remove);
                    buttons.Controls.Add(Ui.Button("&Official guide", () => Process.Start(new ProcessStartInfo(GuideUrl) { UseShellExecute = true })));
                    Ui.Row(layout, "", buttons); form.Controls.Add(layout); Ui.CloseButton(form); form.AcceptButton = insert;
                    list.DoubleClick += delegate { if (list.SelectedIndex >= 0) insert.PerformClick(); };
                    refresh(); form.Shown += delegate { search.Focus(); }; form.ShowDialog(owner);
                }
                target.Focus();
            }
            catch (Exception ex) { Ui.Result(owner, "Could not open speech tags", ex.Message); }
        }
    }
}
