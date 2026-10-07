using System;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class ShortcutButton : Button
    {
        public string ShortcutText { get; set; }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ShortcutButtonAccessibleObject(this);
        }

        private sealed class ShortcutButtonAccessibleObject : Control.ControlAccessibleObject
        {
            private readonly ShortcutButton owner;

            public ShortcutButtonAccessibleObject(ShortcutButton owner) : base(owner)
            {
                this.owner = owner;
            }

            public override string KeyboardShortcut
            {
                get { return string.IsNullOrWhiteSpace(owner.ShortcutText) ? base.KeyboardShortcut : owner.ShortcutText; }
            }
        }
    }
}
