using System.Windows.Input;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (_capturingPttKey)
            {
                e.Handled = true;
                _capturingPttKey = false;
                PttCaptureHint.Visibility = System.Windows.Visibility.Collapsed;
                PttKeyButton.IsEnabled = true;
                if (e.Key != Key.Escape)
                {
                    _pttKey = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                    PttKeyDisplay.Text = _pttKey;
                }
                return;
            }

            if (_capturingKeybindEntry != null)
            {
                e.Handled = true;
                _capturingKeybindEntry.Key = e.Key != Key.Escape
                    ? (new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString())
                    : _capturingKeybindPrevKey;
                _capturingKeybindEntry = null;
                return;
            }

            if (!_isVadMode && !_isPttActive)
            {
                var keyStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                if (keyStr == _pttKey) _isPttActive = true;
            }

            if (!e.IsRepeat)
            {
                var kStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                foreach (var kb in Keybinds)
                {
                    if (kb.Key != kStr) continue;
                    switch (kb.Action)
                    {
                        case "Toggle Mute":   _ = ExecuteToggleMuteAsync();   e.Handled = true; break;
                        case "Toggle Deafen": _ = ExecuteToggleDeafenAsync(); e.Handled = true; break;
                        case "Push to Mute":
                            if (!_pushMuteActive && !_isMuted) { _pushMuteActive = true; _ = ExecuteToggleMuteAsync(); e.Handled = true; }
                            break;
                        case "Push to Deafen":
                            if (!_pushDeafenActive && !_isDeafened) { _pushDeafenActive = true; _ = ExecuteToggleDeafenAsync(); e.Handled = true; }
                            break;
                    }
                }
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (!_isVadMode && _isPttActive)
            {
                var keyStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                if (keyStr == _pttKey) _isPttActive = false;
            }

            var kStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
            foreach (var kb in Keybinds)
            {
                if (kb.Key != kStr) continue;
                switch (kb.Action)
                {
                    case "Push to Mute":
                        if (_pushMuteActive && _isMuted) { _pushMuteActive = false; _ = ExecuteToggleMuteAsync(); }
                        break;
                    case "Push to Deafen":
                        if (_pushDeafenActive && _isDeafened) { _pushDeafenActive = false; _ = ExecuteToggleDeafenAsync(); }
                        break;
                }
            }

            base.OnPreviewKeyUp(e);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (_capturingPttKey)
            {
                e.Handled = true;
                _capturingPttKey = false;
                PttCaptureHint.Visibility = System.Windows.Visibility.Collapsed;
                PttKeyButton.IsEnabled = true;
                _pttKey = e.ChangedButton switch
                {
                    MouseButton.Left     => "Mouse Left",
                    MouseButton.Right    => "Mouse Right",
                    MouseButton.Middle   => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                PttKeyDisplay.Text = _pttKey;
                return;
            }

            if (!_isVadMode && !_isPttActive)
            {
                var mouseStr = e.ChangedButton switch
                {
                    MouseButton.Left     => "Mouse Left",
                    MouseButton.Right    => "Mouse Right",
                    MouseButton.Middle   => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                if (mouseStr == _pttKey) _isPttActive = true;
            }

            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            if (!_isVadMode && _isPttActive)
            {
                var mouseStr = e.ChangedButton switch
                {
                    MouseButton.Left     => "Mouse Left",
                    MouseButton.Right    => "Mouse Right",
                    MouseButton.Middle   => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                if (mouseStr == _pttKey) _isPttActive = false;
            }
            base.OnPreviewMouseUp(e);
        }
    }
}
