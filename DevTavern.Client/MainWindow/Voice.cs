using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;
using NAudio.Wave;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private async void VoiceChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(VoiceChannelList, e); return; }
            if (_selectedProject == null) return;
            if (VoiceChannelList.SelectedItem is not ChannelItem selected) return;
            VoiceChannelList.SelectedIndex = -1;

            if (_currentVoiceChannel == selected) return;
            LeaveCurrentVoiceChannel();
            selected.IsJoined = true;
            selected.VoiceMembers.Add(new VoiceMember { Username = _username, DisplayName = _displayName, AvatarUrl = _avatarUrl });
            _currentVoiceChannel = selected;
            var projectDbId = _projects.FirstOrDefault(p => p.name == _selectedProject)?.DbId ?? 0;
            _currentVoiceGroupKey = $"{projectDbId}_{selected.Name}";
            VoiceConnectedChannelName.Text = $"#{selected.Name}";
            VoiceConnectedBar.Visibility = Visibility.Visible;

            PlaySound("intrare_voice.wav", _soundSettings.JoinVoice);

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("JoinVoiceChannel", _currentVoiceGroupKey, _username); } catch { }

            StartAudioCaptureAndPlayback();
        }

        private VoiceMember? FindVoiceMember(string username)
        {
            foreach (var channels in _projectVoiceChannels.Values)
                foreach (var ch in channels)
                {
                    var m = ch.VoiceMembers.FirstOrDefault(v => v.Username == username);
                    if (m != null) return m;
                }
            return null;
        }

        private string? GetAvatarUrl(string username)
        {
            if (username == _username) return _avatarUrl;
            foreach (var members in _projectMembers.Values)
            {
                var m = members.FirstOrDefault(x => x.Username == username);
                if (!string.IsNullOrEmpty(m?.AvatarUrl)) return m.AvatarUrl;
            }
            return null;
        }

        private string GetDisplayName(string username)
        {
            if (username == _username) return _displayName;
            foreach (var members in _projectMembers.Values)
            {
                var m = members.FirstOrDefault(x => x.Username == username);
                if (m != null && !string.IsNullOrEmpty(m.DisplayName) && m.DisplayName != m.Username) return m.DisplayName;
            }
            if (_fullNameCache.TryGetValue(username, out var name)) return name;
            return username;
        }

        private ChannelItem? FindVoiceChannelByKey(string channelKey)
        {
            foreach (var kvp in _projectVoiceChannels)
            {
                var ch = kvp.Value.FirstOrDefault(c => c.VoiceGroupKey == channelKey);
                if (ch != null) return ch;
            }
            return null;
        }

        private void StartAudioCaptureAndPlayback()
        {
            try
            {
                _waveIn = new WaveInEvent { DeviceNumber = GetWaveInDeviceIndex(InputDeviceCombo.SelectedItem?.ToString()) };
                _waveIn.WaveFormat = new WaveFormat(44100, 16, 1);
                _waveIn.BufferMilliseconds = 100;
                _waveIn.DataAvailable += async (s, args) =>
                {
                    bool transmitting = false;

                    if (!_isMuted && _currentVoiceChannel != null && _currentVoiceGroupKey != null
                        && _hubConnection != null && _hubConnection.State == HubConnectionState.Connected
                        && (_isVadMode || _isPttActive))
                    {
                        byte[] buffer = new byte[args.BytesRecorded];
                        Array.Copy(args.Buffer, buffer, args.BytesRecorded);

                        if (Math.Abs(_inputVolumeScale - 1.0) > 0.01)
                        {
                            for (int i = 0; i < buffer.Length - 1; i += 2)
                            {
                                int sample = BitConverter.ToInt16(buffer, i);
                                sample = Math.Clamp((int)(sample * _inputVolumeScale), short.MinValue, short.MaxValue);
                                buffer[i] = (byte)(sample & 0xFF);
                                buffer[i + 1] = (byte)((sample >> 8) & 0xFF);
                            }
                        }

                        bool vadPassed = !_isVadMode;
                        if (_isVadMode)
                        {
                            double sumSq = 0;
                            int count = buffer.Length / 2;
                            for (int i = 0; i < buffer.Length - 1; i += 2)
                            {
                                double sample = BitConverter.ToInt16(buffer, i);
                                sumSq += sample * sample;
                            }
                            vadPassed = Math.Sqrt(sumSq / count) >= _vadThresholdRms;
                        }

                        if (vadPassed)
                        {
                            transmitting = true;
                            try { await _hubConnection.InvokeAsync("SendAudioBuffer", _currentVoiceGroupKey, _username, buffer); } catch { }
                        }
                    }

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        TransmittingDot.Fill = new SolidColorBrush(transmitting
                            ? Color.FromRgb(0x3F, 0xB9, 0x50)
                            : Color.FromRgb(0x48, 0x4F, 0x58));
                        var localMember = FindVoiceMember(_username);
                        if (localMember != null) localMember.IsSpeaking = transmitting;
                    });
                };

                _waveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 1));
                _waveProvider.DiscardOnBufferOverflow = true;

                _waveOut = new WaveOutEvent { DeviceNumber = GetWaveOutDeviceIndex(OutputDeviceCombo.SelectedItem?.ToString()) };
                _waveOut.Init(_waveProvider);
                _waveOut.Play();
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start audio devices: {ex.Message}");
            }
        }

        private async void LeaveCurrentVoiceChannel()
        {
            VoiceConnectedBar.Visibility = Visibility.Collapsed;
            ResetMuteDeafen();
            StopAudioCaptureAndPlayback();

            if (_currentVoiceChannel != null)
            {
                var leavingChannel = _currentVoiceChannel;
                var leavingKey = _currentVoiceGroupKey;
                _currentVoiceChannel = null;
                _currentVoiceGroupKey = null;

                PlaySound("iesire_voice.wav", _soundSettings.LeaveVoice);

                leavingChannel.IsJoined = false;
                var selfMember = leavingChannel.VoiceMembers.FirstOrDefault(m => m.Username == _username);
                if (selfMember != null) leavingChannel.VoiceMembers.Remove(selfMember);

                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected && leavingKey != null)
                    try { await _hubConnection.InvokeAsync("LeaveVoiceChannel", leavingKey, _username); } catch { }
            }
        }

        private void StopAudioCaptureAndPlayback()
        {
            if (_waveIn != null)
            {
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }
            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }
            _waveProvider = null;
        }

        private void ResetMuteDeafen()
        {
            _isMuted = false;
            _isDeafened = false;
            MuteIcon.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            DeafenIcon.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
        }

        private async void MuteVoice_Click(object sender, RoutedEventArgs e) => await ExecuteToggleMuteAsync();
        private async void DeafenVoice_Click(object sender, RoutedEventArgs e) => await ExecuteToggleDeafenAsync();

        private async Task ExecuteToggleMuteAsync()
        {
            _isMuted = !_isMuted;
            if (_isMuted) PlaySound("mute.wav", _soundSettings.Mute);
            else PlaySound("unmute.wav", _soundSettings.Unmute);
            MuteIcon.Fill = new SolidColorBrush(_isMuted
                ? Color.FromRgb(0xDA, 0x36, 0x33) : Color.FromRgb(0x8B, 0x94, 0x9E));
            var me = _currentVoiceChannel?.VoiceMembers.FirstOrDefault(m => m.Username == _username);
            if (me != null) me.IsMuted = _isMuted;
            if (_currentVoiceGroupKey != null && _hubConnection?.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("BroadcastVoiceState", _currentVoiceGroupKey, _username, _isMuted, _isDeafened); } catch { }
        }

        private async Task ExecuteToggleDeafenAsync()
        {
            _isDeafened = !_isDeafened;
            if (_isDeafened) PlaySound("mute.wav", _soundSettings.Deafen);
            else PlaySound("unmute.wav", _soundSettings.Undeafen);
            DeafenIcon.Fill = new SolidColorBrush(_isDeafened
                ? Color.FromRgb(0xDA, 0x36, 0x33) : Color.FromRgb(0x8B, 0x94, 0x9E));
            var me = _currentVoiceChannel?.VoiceMembers.FirstOrDefault(m => m.Username == _username);
            if (me != null) me.IsDeafened = _isDeafened;
            if (_currentVoiceGroupKey != null && _hubConnection?.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("BroadcastVoiceState", _currentVoiceGroupKey, _username, _isMuted, _isDeafened); } catch { }
        }

        private void LeaveVoiceChannel_Click(object sender, RoutedEventArgs e) => LeaveCurrentVoiceChannel();

        private void VoiceChannelList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void AddVoiceChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProject == null) return;
            NewVoiceChannelNameInput.Text = "";
            AddVoiceChannelOverlay.Visibility = Visibility.Visible;
            NewVoiceChannelNameInput.Focus();
        }

        private void CancelAddVoiceChannel_Click(object sender, RoutedEventArgs e)
            => AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;

        private void AddVoiceChannelOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;

        private async void ConfirmAddVoiceChannel_Click(object sender, RoutedEventArgs e)
        {
            var name = NewVoiceChannelNameInput.Text.Trim();
            if (string.IsNullOrEmpty(name) || _selectedProject == null) return;
            if (!_projectVoiceChannels.TryGetValue(_selectedProject, out var voiceChannels)) return;

            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || proj.DbId == 0) return;

            if (voiceChannels.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A voice channel with this name already exists.", "DevTavern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var postData = new { Name = name, Type = 2 };
                var content = new System.Net.Http.StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                var resp = await _apiClient.PostAsync($"channels/project/{proj.DbId}", content);
                if (resp.IsSuccessStatusCode)
                {
                    var cJson = Newtonsoft.Json.Linq.JObject.Parse(await resp.Content.ReadAsStringAsync());
                    voiceChannels.Add(new ChannelItem
                    {
                        Id = cJson["id"]?.ToObject<int>() ?? 0,
                        Name = name,
                        Type = 2,
                        VoiceGroupKey = $"{proj.DbId}_{name}"
                    });
                }
            }
            catch { }

            AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void NewVoiceChannelNameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ConfirmAddVoiceChannel_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Escape) { AddVoiceChannelOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
        }
    }
}
