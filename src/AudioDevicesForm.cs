using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave;
namespace ElevenLabsSpeechGenerator
{
    internal static class AudioDevicesForm
    {
        internal static ComboBox DeviceList(int selected)
        {
            var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Playback device" };
            list.Items.Add("System default");
            for (int i = 0; i < WaveOut.DeviceCount; i++) list.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            list.SelectedIndex = selected >= -1 && selected < WaveOut.DeviceCount ? selected + 1 : 0;
            return list;
        }
    }
}
