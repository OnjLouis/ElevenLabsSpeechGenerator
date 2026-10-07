using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class ShortcutComboBox : ComboBox
    {
        public string ShortcutText { get; set; }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ShortcutAccessibleObject(this);
        }

        private sealed class ShortcutAccessibleObject : Control.ControlAccessibleObject
        {
            private readonly ShortcutComboBox owner;

            public ShortcutAccessibleObject(ShortcutComboBox owner) : base(owner)
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
