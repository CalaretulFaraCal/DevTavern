using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private void ServerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProject == null) return;
            _settingsTargetProject = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (_settingsTargetProject == null) return;

            ServerSettingsProjectName.Text = _settingsTargetProject.name;
            ServerSettingsIconInitials.Text = _settingsTargetProject.IconLetters;

            if (_settingsTargetProject.HasCustomImage && _settingsTargetProject.CustomImageSource != null)
            {
                ServerSettingsIconBrush.ImageSource = _settingsTargetProject.CustomImageSource;
                ServerSettingsIconImageEllipse.Visibility = Visibility.Visible;
                ServerSettingsRemoveBtn.Visibility = Visibility.Visible;
            }
            else
            {
                ServerSettingsIconBrush.ImageSource = null;
                ServerSettingsIconImageEllipse.Visibility = Visibility.Collapsed;
                ServerSettingsRemoveBtn.Visibility = Visibility.Collapsed;
            }

            ServerSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void ServerSettingsOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => ServerSettingsOverlay.Visibility = Visibility.Collapsed;

        private void CloseServerSettings_Click(object sender, RoutedEventArgs e)
            => ServerSettingsOverlay.Visibility = Visibility.Collapsed;

        private void ChangeServerIcon_Click(object sender, RoutedEventArgs e)
        {
            var fileDlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Server Icon",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Multiselect = false
            };
            if (fileDlg.ShowDialog() != true || _settingsTargetProject == null) return;
            OpenCropOverlay(fileDlg.FileName);
        }

        private async void RemoveServerIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsTargetProject == null) return;
            _settingsTargetProject.CustomImagePath = null;
            _settingsTargetProject.ServerImageUrl = null;
            SaveServerIcons();

            if (_settingsTargetProject.DbId > 0)
            {
                try
                {
                    var body = new System.Net.Http.StringContent(
                        JsonConvert.SerializeObject(new { ImageUrl = (string?)null }),
                        System.Text.Encoding.UTF8, "application/json");
                    await _apiClient.PutAsync($"projects/{_settingsTargetProject.DbId}/image", body);
                }
                catch { }
            }

            ServerSettingsIconBrush.ImageSource = null;
            ServerSettingsIconImageEllipse.Visibility = Visibility.Collapsed;
            ServerSettingsRemoveBtn.Visibility = Visibility.Collapsed;
        }

        private void OpenCropOverlay(string filePath)
        {
            _cropBitmap = new BitmapImage();
            _cropBitmap.BeginInit();
            _cropBitmap.UriSource = new Uri(filePath);
            _cropBitmap.CacheOption = BitmapCacheOption.OnLoad;
            _cropBitmap.EndInit();

            CropImageElement.Source = _cropBitmap;
            _cropOrigWidth  = _cropBitmap.PixelWidth;
            _cropOrigHeight = _cropBitmap.PixelHeight;

            double minDim = Math.Min(_cropOrigWidth, _cropOrigHeight);
            _cropScale = (CropCircleRadius * 2.0) / minDim;
            _cropTx = 0;
            _cropTy = 0;

            UpdateCropTransform();
            CropImageOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateCropTransform()
        {
            double imgW = _cropOrigWidth  * _cropScale;
            double imgH = _cropOrigHeight * _cropScale;
            System.Windows.Controls.Canvas.SetLeft(CropImageElement, (CropViewportSize - imgW) / 2.0 + _cropTx);
            System.Windows.Controls.Canvas.SetTop(CropImageElement,  (CropViewportSize - imgH) / 2.0 + _cropTy);
            CropImageElement.Width  = imgW;
            CropImageElement.Height = imgH;
        }

        private void ClampCropTranslation()
        {
            double imgW = _cropOrigWidth  * _cropScale;
            double imgH = _cropOrigHeight * _cropScale;
            double imgL = (CropViewportSize - imgW) / 2 + _cropTx;
            double imgT = (CropViewportSize - imgH) / 2 + _cropTy;

            double cropL = CropViewportSize / 2 - CropCircleRadius;
            double cropR = CropViewportSize / 2 + CropCircleRadius;
            double cropT = CropViewportSize / 2 - CropCircleRadius;
            double cropB = CropViewportSize / 2 + CropCircleRadius;

            if (imgL > cropL) _cropTx -= imgL - cropL;
            if (imgL + imgW < cropR) _cropTx += cropR - (imgL + imgW);
            if (imgT > cropT) _cropTy -= imgT - cropT;
            if (imgT + imgH < cropB) _cropTy += cropB - (imgT + imgH);
        }

        private void CropViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            _cropIsDragging = true;
            _cropLastMouse  = e.GetPosition(CropViewportBorder);
            CropViewportBorder.CaptureMouse();
            e.Handled = true;
        }

        private void CropViewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_cropIsDragging) return;
            Point pos = e.GetPosition(CropViewportBorder);
            _cropTx += pos.X - _cropLastMouse.X;
            _cropTy += pos.Y - _cropLastMouse.Y;
            _cropLastMouse = pos;
            ClampCropTranslation();
            UpdateCropTransform();
        }

        private void CropViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _cropIsDragging = false;
            CropViewportBorder.ReleaseMouseCapture();
        }

        private void CropViewport_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double minDim   = Math.Min(_cropOrigWidth, _cropOrigHeight);
            double minScale = (CropCircleRadius * 2.0) / minDim;
            _cropScale = Math.Clamp(_cropScale * factor, minScale, minScale * 15.0);
            ClampCropTranslation();
            UpdateCropTransform();
        }

        private void CropOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => CropImageOverlay.Visibility = Visibility.Collapsed;

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
            => CropImageOverlay.Visibility = Visibility.Collapsed;

        private async void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_cropBitmap == null || _settingsTargetProject == null) return;

            const int    outputSize   = 300;
            const double cornerRadius = 95.0;

            var rtb = new RenderTargetBitmap(outputSize, outputSize, 96, 96, PixelFormats.Pbgra32);
            var dv  = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, outputSize, outputSize), cornerRadius, cornerRadius));
                double imgW = _cropOrigWidth  * _cropScale;
                double imgH = _cropOrigHeight * _cropScale;
                double relLeft = (CropViewportSize - imgW) / 2.0 + _cropTx - (CropViewportSize / 2.0 - CropCircleRadius);
                double relTop  = (CropViewportSize - imgH) / 2.0 + _cropTy - (CropViewportSize / 2.0 - CropCircleRadius);
                dc.DrawImage(_cropBitmap, new Rect(relLeft, relTop, imgW, imgH));
                dc.Pop();
            }
            rtb.Render(dv);

            string iconsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevTavern", "ServerIcons");
            Directory.CreateDirectory(iconsDir);
            string outPath = Path.Combine(iconsDir, $"{_settingsTargetProject.id}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var stream = File.Create(outPath))
                encoder.Save(stream);

            _settingsTargetProject.CustomImagePath = outPath;
            SaveServerIcons();

            if (_settingsTargetProject.DbId > 0)
            {
                try
                {
                    var bytes = File.ReadAllBytes(outPath);
                    var base64 = Convert.ToBase64String(bytes);
                    var dataUrl = $"data:image/png;base64,{base64}";
                    var body = new System.Net.Http.StringContent(
                        JsonConvert.SerializeObject(new { ImageUrl = dataUrl }),
                        System.Text.Encoding.UTF8, "application/json");
                    var resp = await _apiClient.PutAsync($"projects/{_settingsTargetProject.DbId}/image", body);
                    if (resp.IsSuccessStatusCode) _settingsTargetProject.ServerImageUrl = dataUrl;
                }
                catch { }
            }

            ServerSettingsIconBrush.ImageSource = _settingsTargetProject.CustomImageSource;
            ServerSettingsIconImageEllipse.Visibility = Visibility.Visible;
            ServerSettingsRemoveBtn.Visibility = Visibility.Visible;
            CropImageOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
