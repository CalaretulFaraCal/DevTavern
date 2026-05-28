using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private Point _listboxDownPos;
        private bool  _listboxDragging = false;
        private bool  _listboxButtonHeld = false;
        private bool  _restoringSelection = false;

        private bool IsDragSelection()
        {
            if (!_listboxButtonHeld) return false;
            var pos = Mouse.GetPosition(this);
            return Math.Abs(pos.X - _listboxDownPos.X) > 2 || Math.Abs(pos.Y - _listboxDownPos.Y) > 2;
        }

        private void CancelDragSelection(ListBox listBox, SelectionChangedEventArgs e)
        {
            _restoringSelection = true;
            listBox.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            _restoringSelection = false;
        }

        private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_listboxButtonHeld)
            {
                _listboxDragging = true;
                e.Handled = true;
                return;
            }
            _listboxDownPos = e.GetPosition(this);
            _listboxButtonHeld = true;
            _listboxDragging = false;
        }

        private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _listboxDragging = false; _listboxButtonHeld = false; return; }
            if (!_listboxDragging)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - _listboxDownPos.X) > 2 || Math.Abs(pos.Y - _listboxDownPos.Y) > 2)
                    _listboxDragging = true;
            }
            if (_listboxDragging) e.Handled = true;
        }

        private void ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _listboxDragging = false;
            _listboxButtonHeld = false;
        }

        private void ChannelList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                ((UIElement)sender).RaiseEvent(eventArg);
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (UserProfilePopup.IsOpen)
            {
                var popupBorder = UserProfilePopup.Child as FrameworkElement;
                if (popupBorder != null)
                {
                    var pos = e.GetPosition(popupBorder);
                    if (pos.X < 0 || pos.Y < 0 || pos.X > popupBorder.ActualWidth || pos.Y > popupBorder.ActualHeight)
                        UserProfilePopup.IsOpen = false;
                }
            }

            if (MiniProfilePanel.Visibility != Visibility.Visible) return;
            var mpos = e.GetPosition(MiniProfilePanel);
            if (mpos.X < 0 || mpos.Y < 0 || mpos.X > MiniProfilePanel.ActualWidth || mpos.Y > MiniProfilePanel.ActualHeight)
            {
                MiniProfilePanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void ToggleMembersButton_Click(object sender, RoutedEventArgs e)
        {
            if (_codeBrowserVisible)
            {
                CodeBrowserPanel.Visibility = Visibility.Collapsed;
                CodeBrowserSplitter.Visibility = Visibility.Collapsed;
                SplitterColumn.Width = new GridLength(0);
                _codeBrowserVisible = false;
            }

            _membersPanelVisible = !_membersPanelVisible;

            if (_membersPanelVisible)
            {
                MembersPanelColumn.Width = new GridLength(240);
                MembersPanelBorder.Visibility = Visibility.Visible;
            }
            else
            {
                MembersPanelColumn.Width = new GridLength(0);
                MembersPanelBorder.Visibility = Visibility.Collapsed;
            }
        }
    }
}
