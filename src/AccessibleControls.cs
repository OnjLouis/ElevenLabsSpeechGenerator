using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ElevenLabsSpeechGenerator
{
    internal class ShortcutTextBox : TextBox
    {
        public string ShortcutText { get; set; }
        private const int GetFirstVisibleLine = 0xCE;
        private const int ScrollLines = 0xB6;
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public void UpdateReadableText(string value, int removedPrefix = 0)
        {
            bool reading = Focused;
            int start = Math.Max(0, SelectionStart - removedPrefix), length = SelectionLength;
            int firstLine = IsHandleCreated ? SendMessage(Handle, GetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
            int removedLines = 0;
            for (int i = 0; i < Math.Min(removedPrefix, TextLength); i++) if (Text[i] == '\n') removedLines++;
            Text = value;
            if (reading)
            {
                start = Math.Min(start, TextLength); Select(start, Math.Min(length, TextLength - start));
                if (IsHandleCreated)
                {
                    int current = SendMessage(Handle, GetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    SendMessage(Handle, ScrollLines, IntPtr.Zero, new IntPtr(Math.Max(0, firstLine - removedLines) - current));
                }
            }
            else { Select(TextLength, 0); ScrollToCaret(); }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ShortcutTextBoxAccessibleObject(this);
        }

        private sealed class ShortcutTextBoxAccessibleObject : Control.ControlAccessibleObject
        {
            private readonly ShortcutTextBox owner;

            public ShortcutTextBoxAccessibleObject(ShortcutTextBox owner) : base(owner)
            {
                this.owner = owner;
            }

            public override string KeyboardShortcut
            {
                get { return string.IsNullOrWhiteSpace(owner.ShortcutText) ? base.KeyboardShortcut : owner.ShortcutText; }
            }
        }
    }

    internal sealed class AccessibleStatusTextBox : ShortcutTextBox
    {
        public void NotifyValueChanged()
        {
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }

    internal sealed class AccessibleStatusLabel : Label
    {
        public void NotifyNameChanged()
        {
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }
}
