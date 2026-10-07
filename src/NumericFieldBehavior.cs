using System;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class NumericFieldBehavior
    {
        public static void SelectCurrentValueOnFocus(NumericUpDown control)
        {
            control.Enter += delegate
            {
                control.BeginInvoke((MethodInvoker)delegate
                {
                    control.Select(0, control.Text.Length);
                });
            };
        }
    }
}
