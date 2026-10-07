using System;
using System.Drawing;
using System.Windows.Forms;
namespace ElevenLabsSpeechGenerator
{
    internal static class Ui
    {
        public static TableLayoutPanel Layout(int columns)
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, AutoSize = false, ColumnCount = columns, Padding = new Padding(12) };
            if (columns == 2) { p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); }
            return p;
        }
        public static void Row(TableLayoutPanel p, string label, Control control)
        {
            int r = p.RowCount++; p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            p.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 12, 3) }, 0, r);
            control.Dock = DockStyle.Fill; p.Controls.Add(control, 1, r);
        }
        public static Button Button(string text, Action action)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 28) }; b.Click += delegate { action(); }; return b;
        }
        public static ComboBox Combo(string name, object[] values)
        {
            var c = new ComboBox { AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 }; c.Items.AddRange(values); if (values.Length > 0) c.SelectedIndex = 0; return c;
        }
        public static TextBox Text(string name, bool multiline)
        {
            return new TextBox { AccessibleName = name, Multiline = multiline, AcceptsReturn = multiline, AcceptsTab = false, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, Height = multiline ? 120 : 25 };
        }
        public static NumericUpDown Number(string name, decimal min, decimal max, decimal value, int decimals)
        {
            var n = new NumericUpDown { AccessibleName = name, Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Increment = decimals > 0 ? .1m : 1, Width = 110 };
            NumericFieldBehavior.SelectCurrentValueOnFocus(n); return n;
        }
        public static Form Dialog(string title, Size size)
        {
            return new Form { Text = title, AccessibleName = title, Size = size, MinimumSize = new Size(480, 300), ShowInTaskbar = false, ShowIcon = false, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent };
        }
        public static void CloseButton(Form f)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var b = Button("&Close", () => f.Close()); p.Controls.Add(b); f.Controls.Add(p); f.CancelButton = b;
        }
        public static void Result(IWin32Window owner, string title, string text)
        {
            using (var f = new ApiKeyTestResultForm(SpeechProject.WindowsLines(text), title)) { f.ShowDialog(owner); }
        }
    }
}
