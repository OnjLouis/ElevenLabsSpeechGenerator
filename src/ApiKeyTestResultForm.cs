using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class ApiKeyTestResultForm : Form
    {
        public ApiKeyTestResultForm(string result, string title = "API key test result", Action openManual = null)
        {
            Text = title;
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 280);
            MinimumSize = new Size(480, 200);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var resultTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = true,
                AccessibleName = title,
                Text = result
            };
            var closeButton = new Button { Text = "&Close", AutoSize = true, DialogResult = DialogResult.OK, TabIndex = 1 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(closeButton);
            if (openManual != null)
            {
                var manualButton = new Button { Text = "Open &manual", AutoSize = true, TabIndex = 0 };
                manualButton.Click += delegate { openManual(); };
                buttons.Controls.Add(manualButton);
            }
            layout.Controls.Add(resultTextBox, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = closeButton;
            CancelButton = closeButton;
            Shown += delegate { resultTextBox.Focus(); resultTextBox.Select(0, 0); };
        }
    }
}
