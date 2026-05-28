using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private class VoiceSettings
        {
            public double InputVolume { get; set; } = 100;
            public double OutputVolume { get; set; } = 100;
            public double Sensitivity { get; set; } = -40;
            public bool IsPttMode { get; set; } = false;
            public string PttKey { get; set; } = "Caps Lock";
            public string InputDevice { get; set; } = "";
            public string OutputDevice { get; set; } = "";
        }

        private class SoundConfig
        {
            public bool Enabled { get; set; } = true;
            public double Volume { get; set; } = 50;
        }

        private class AppSoundSettings
        {
            public SoundConfig Startup { get; set; } = new();
            public SoundConfig JoinVoice { get; set; } = new();
            public SoundConfig LeaveVoice { get; set; } = new();
            public SoundConfig Mute { get; set; } = new();
            public SoundConfig Unmute { get; set; } = new();
            public SoundConfig Deafen { get; set; } = new();
            public SoundConfig Undeafen { get; set; } = new();
            public SoundConfig Message { get; set; } = new();
            public SoundConfig Mention { get; set; } = new();
        }

        private void PopulateAudioDevices()
        {
            var prevInput = InputDeviceCombo.SelectedItem?.ToString();
            var prevOutput = OutputDeviceCombo.SelectedItem?.ToString();

            InputDeviceCombo.Items.Clear();
            OutputDeviceCombo.Items.Clear();

            try
            {
                var enumerator = new MMDeviceEnumerator();
                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    InputDeviceCombo.Items.Add(d.FriendlyName);
                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    OutputDeviceCombo.Items.Add(d.FriendlyName);
            }
            catch { }

            if (InputDeviceCombo.Items.Count == 0) InputDeviceCombo.Items.Add("No input device");
            if (OutputDeviceCombo.Items.Count == 0) OutputDeviceCombo.Items.Add("No output device");

            RestoreDeviceSelection(prevInput, prevOutput);
        }

        private int GetWaveInDeviceIndex(string? friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)) return 0;
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var name = WaveIn.GetCapabilities(i).ProductName;
                if (friendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private int GetWaveOutDeviceIndex(string? friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)) return 0;
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var name = WaveOut.GetCapabilities(i).ProductName;
                if (friendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private void RestoreDeviceSelection(string? inputName, string? outputName)
        {
            InputDeviceCombo.SelectedIndex = 0;
            for (int i = 0; i < InputDeviceCombo.Items.Count; i++)
                if (InputDeviceCombo.Items[i]?.ToString() == inputName) { InputDeviceCombo.SelectedIndex = i; break; }

            OutputDeviceCombo.SelectedIndex = 0;
            for (int i = 0; i < OutputDeviceCombo.Items.Count; i++)
                if (OutputDeviceCombo.Items[i]?.ToString() == outputName) { OutputDeviceCombo.SelectedIndex = i; break; }
        }

        private void LoadVoiceSettings()
        {
            try
            {
                if (!File.Exists(_voiceSettingsPath)) return;
                var s = JsonConvert.DeserializeObject<VoiceSettings>(File.ReadAllText(_voiceSettingsPath));
                if (s == null) return;

                InputVolumeSlider.Value = s.InputVolume;
                OutputVolumeSlider.Value = s.OutputVolume;
                SensitivitySlider.Value = s.Sensitivity;
                _pttKey = s.PttKey;
                PttKeyDisplay.Text = s.PttKey;

                if (s.IsPttMode) PttModeRadio.IsChecked = true;
                else VadModeRadio.IsChecked = true;

                RestoreDeviceSelection(s.InputDevice, s.OutputDevice);
            }
            catch { }
        }

        private void SaveVoiceSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_voiceSettingsPath)!);
                var s = new VoiceSettings
                {
                    InputVolume = InputVolumeSlider.Value,
                    OutputVolume = OutputVolumeSlider.Value,
                    Sensitivity = SensitivitySlider.Value,
                    IsPttMode = PttModeRadio.IsChecked == true,
                    PttKey = _pttKey,
                    InputDevice = InputDeviceCombo.SelectedItem?.ToString() ?? "",
                    OutputDevice = OutputDeviceCombo.SelectedItem?.ToString() ?? ""
                };
                File.WriteAllText(_voiceSettingsPath, JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { }
        }

        private static AppSoundSettings LoadSoundSettings()
        {
            try
            {
                if (File.Exists(_soundSettingsPath))
                {
                    var loaded = JsonConvert.DeserializeObject<AppSoundSettings>(File.ReadAllText(_soundSettingsPath));
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return new AppSoundSettings();
        }

        private void SaveSoundSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_soundSettingsPath)!);
                File.WriteAllText(_soundSettingsPath, JsonConvert.SerializeObject(_soundSettings, Formatting.Indented));
            }
            catch { }
        }

        private void LoadSoundSettingsToUI()
        {
            SoundJoinVoiceCheck.IsChecked = _soundSettings.JoinVoice.Enabled;
            SoundJoinVoiceSlider.Value = _soundSettings.JoinVoice.Volume;
            SoundLeaveVoiceCheck.IsChecked = _soundSettings.LeaveVoice.Enabled;
            SoundLeaveVoiceSlider.Value = _soundSettings.LeaveVoice.Volume;
            SoundMuteCheck.IsChecked = _soundSettings.Mute.Enabled;
            SoundMuteSlider.Value = _soundSettings.Mute.Volume;
            SoundUnmuteCheck.IsChecked = _soundSettings.Unmute.Enabled;
            SoundUnmuteSlider.Value = _soundSettings.Unmute.Volume;
            SoundDeafenCheck.IsChecked = _soundSettings.Deafen.Enabled;
            SoundDeafenSlider.Value = _soundSettings.Deafen.Volume;
            SoundUndeafenCheck.IsChecked = _soundSettings.Undeafen.Enabled;
            SoundUndeafenSlider.Value = _soundSettings.Undeafen.Volume;
            SoundMessageCheck.IsChecked = _soundSettings.Message.Enabled;
            SoundMessageSlider.Value = _soundSettings.Message.Volume;
            SoundMentionCheck.IsChecked = _soundSettings.Mention.Enabled;
            SoundMentionSlider.Value = _soundSettings.Mention.Volume;
        }

        private void SaveSoundSettingsFromUI()
        {
            _soundSettings.JoinVoice.Enabled = SoundJoinVoiceCheck.IsChecked == true;
            _soundSettings.JoinVoice.Volume = SoundJoinVoiceSlider.Value;
            _soundSettings.LeaveVoice.Enabled = SoundLeaveVoiceCheck.IsChecked == true;
            _soundSettings.LeaveVoice.Volume = SoundLeaveVoiceSlider.Value;
            _soundSettings.Mute.Enabled = SoundMuteCheck.IsChecked == true;
            _soundSettings.Mute.Volume = SoundMuteSlider.Value;
            _soundSettings.Unmute.Enabled = SoundUnmuteCheck.IsChecked == true;
            _soundSettings.Unmute.Volume = SoundUnmuteSlider.Value;
            _soundSettings.Deafen.Enabled = SoundDeafenCheck.IsChecked == true;
            _soundSettings.Deafen.Volume = SoundDeafenSlider.Value;
            _soundSettings.Undeafen.Enabled = SoundUndeafenCheck.IsChecked == true;
            _soundSettings.Undeafen.Volume = SoundUndeafenSlider.Value;
            _soundSettings.Message.Enabled = SoundMessageCheck.IsChecked == true;
            _soundSettings.Message.Volume = SoundMessageSlider.Value;
            _soundSettings.Mention.Enabled = SoundMentionCheck.IsChecked == true;
            _soundSettings.Mention.Volume = SoundMentionSlider.Value;
        }

        private void VoiceSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void ApplicationSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void ProfileSettings_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
            VoiceTabContent.Visibility = Visibility.Visible;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, true);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void PopupSettings_Click(object sender, MouseButtonEventArgs e)
        {
            UserProfilePopup.IsOpen = false;
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Visible;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, true);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void VoiceSettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_capturingPttKey) { _capturingPttKey = false; PttCaptureHint.Visibility = Visibility.Collapsed; PttKeyButton.IsEnabled = true; }
            _capturingKeybindEntry = null;
            SaveVoiceSettings();
            SaveSoundSettingsFromUI();
            SaveSoundSettings();
            SaveKeybinds();
            VoiceSettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseVoiceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingPttKey) { _capturingPttKey = false; PttCaptureHint.Visibility = Visibility.Collapsed; PttKeyButton.IsEnabled = true; }
            _capturingKeybindEntry = null;
            SaveVoiceSettings();
            SaveSoundSettingsFromUI();
            SaveSoundSettings();
            SaveKeybinds();
            VoiceSettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void SettingsTabVoice_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Visible;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, true);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void SettingsTabApp_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Visible;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, true);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void SettingsTabKeybinds_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Visible;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, true);
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void SetTabActive(Button tab, bool active)
        {
            var border = FindVisualChild<Border>(tab);
            var text = FindVisualChild<TextBlock>(tab);
            var green = new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36));
            var grey = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            if (border != null) border.BorderBrush = active ? green : Brushes.Transparent;
            if (text != null) text.Foreground = active ? green : grey;
        }

        private void VadMode_Checked(object sender, RoutedEventArgs e)
        {
            if (SensitivityPanel == null) return;
            SensitivityPanel.Visibility = Visibility.Visible;
            PttKeyPanel.Visibility = Visibility.Collapsed;
            _isVadMode = true;
        }

        private void PttMode_Checked(object sender, RoutedEventArgs e)
        {
            if (PttKeyPanel == null) return;
            SensitivityPanel.Visibility = Visibility.Collapsed;
            PttKeyPanel.Visibility = Visibility.Visible;
            _isVadMode = false;
        }

        private void PttKeyButton_Click(object sender, RoutedEventArgs e)
        {
            _capturingPttKey = true;
            PttKeyButton.IsEnabled = false;
            PttCaptureHint.Visibility = Visibility.Visible;
        }

        private void CaptureKeybind_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not KeybindEntry entry) return;
            _capturingKeybindPrevKey = entry.Key;
            _capturingKeybindEntry = entry;
            entry.Key = "Press a key...";
        }

        private void AddKeybind_Click(object sender, RoutedEventArgs e)
        {
            Keybinds.Add(new KeybindEntry());
            SaveKeybinds();
        }

        private void DeleteKeybind_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not KeybindEntry entry) return;
            Keybinds.Remove(entry);
            SaveKeybinds();
        }

        private List<KeybindEntry> LoadKeybindList()
        {
            try
            {
                if (File.Exists(_keybindsPath))
                {
                    var loaded = JsonConvert.DeserializeObject<List<KeybindEntry>>(File.ReadAllText(_keybindsPath));
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return new List<KeybindEntry>();
        }

        private void SaveKeybinds()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_keybindsPath)!);
                File.WriteAllText(_keybindsPath, JsonConvert.SerializeObject(Keybinds.ToList(), Formatting.Indented));
            }
            catch { }
        }

        private void PreviewJoinVoice_Click(object sender, RoutedEventArgs e)
            => PlaySound("intrare_voice.wav", new SoundConfig { Enabled = true, Volume = SoundJoinVoiceSlider.Value });
        private void PreviewLeaveVoice_Click(object sender, RoutedEventArgs e)
            => PlaySound("iesire_voice.wav", new SoundConfig { Enabled = true, Volume = SoundLeaveVoiceSlider.Value });
        private void PreviewMute_Click(object sender, RoutedEventArgs e)
            => PlaySound("mute.wav", new SoundConfig { Enabled = true, Volume = SoundMuteSlider.Value });
        private void PreviewUnmute_Click(object sender, RoutedEventArgs e)
            => PlaySound("unmute.wav", new SoundConfig { Enabled = true, Volume = SoundUnmuteSlider.Value });
        private void PreviewDeafen_Click(object sender, RoutedEventArgs e)
            => PlaySound("mute.wav", new SoundConfig { Enabled = true, Volume = SoundDeafenSlider.Value });
        private void PreviewUndeafen_Click(object sender, RoutedEventArgs e)
            => PlaySound("unmute.wav", new SoundConfig { Enabled = true, Volume = SoundUndeafenSlider.Value });
        private void PreviewMessage_Click(object sender, RoutedEventArgs e)
            => PlaySound("mesaje.wav", new SoundConfig { Enabled = true, Volume = SoundMessageSlider.Value });
        private void PreviewMention_Click(object sender, RoutedEventArgs e)
            => PlaySound("mention.wav", new SoundConfig { Enabled = true, Volume = SoundMentionSlider.Value });
    }
}
