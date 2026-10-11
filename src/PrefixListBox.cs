using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    // Keep native list accessibility, but accumulate letters instead of jumping for each one.
    internal sealed class PrefixListBox : ListBox
    {
        private const int SearchPauseMilliseconds = 1000;
        private readonly Stopwatch sinceTyping = new Stopwatch();
        private string prefix = "";
        internal void ResetSearch() { prefix = ""; sinceTyping.Reset(); }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar) || (ModifierKeys & (Keys.Control | Keys.Alt)) != Keys.None) { base.OnKeyPress(e); return; }
            e.Handled = true;
            if (sinceTyping.ElapsedMilliseconds > SearchPauseMilliseconds) ResetSearch();
            bool cycle = prefix.Length == 1 && string.Equals(prefix, e.KeyChar.ToString(), StringComparison.CurrentCultureIgnoreCase);
            bool fresh = prefix.Length == 0;
            if (!cycle) prefix += e.KeyChar;
            sinceTyping.Restart(); SelectPrefix(fresh || cycle);
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
            if (e.KeyCode == Keys.Back && !e.Control && !e.Alt) {
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
}
