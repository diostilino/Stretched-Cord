using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StretchCord.Audio;
using StretchCord.Capture;
using StretchCord.Models;
using StretchCord.Services;
using NAudio.CoreAudioApi;

namespace StretchCord.UI
{
    public partial class MainWindow : Window
    {
        // ── Services ─────────────────────────────────────────────────────────
        private readonly GraphicsCaptureService _captureService = new();
        private readonly ApplicationLoopbackCapture _audioCapture = new();
        private readonly AudioPlayer _audioPlayer = new();

        // ── State ────────────────────────────────────────────────────────────
        private WindowInfo? _selectedWindow;
        private TransmissionWindow? _streamWindow;
        private bool _videoActive;
        private bool _audioActive;

        // ── Preview write-back bitmap (software path) ─────────────────────
        private WriteableBitmap? _previewBitmap;

        // ── Audio level smoothing ─────────────────────────────────────────
        private float _smoothedLevel;

        public MainWindow()
        {
            InitializeComponent();

            if (!GraphicsCaptureService.IsSupported())
            {
                MessageBox.Show(
                    "Windows.Graphics.Capture is not available on this system.\n" +
                    "Requires Windows 10 version 1903 (May 2019 Update) or later.",
                    "StretchCord — Unsupported", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            try { _captureService.Initialize(); }
            catch (Exception ex)
            {
                SetStatus($"D3D init error: {ex.Message}");
            }

            // Wire capture events
            _captureService.FrameArrived += OnFrameArrived;
            _captureService.Error += msg => Dispatcher.InvokeAsync(() => SetStatus(msg));

            // Wire audio events
            _audioCapture.AudioDataAvailable += OnAudioData;
            _audioCapture.LevelChanged += OnAudioLevel;
            _audioCapture.Error += msg => Dispatcher.InvokeAsync(() =>
            {
                SetStatus($"Audio: {msg}");
                MessageBox.Show(msg, "Audio Capture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StopAudio();
            });

            LoadAudioOutputDevices();
        }

        // ════════════════════════════════════════════════════════════════════
        // UI Events
        // ════════════════════════════════════════════════════════════════════

        private void BtnSelectWindow_Click(object sender, RoutedEventArgs e)
        {
            var picker = new WindowPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedWindowInfo != null)
            {
                _selectedWindow = picker.SelectedWindowInfo;
                TxtSelectedWindow.Text = _selectedWindow.ToString();
                TxtPid.Text = $"PID: {_selectedWindow.ProcessId}";
                TxtSelectedWindow.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0));
                BtnToggleVideo.IsEnabled = true;
                BtnToggleAudio.IsEnabled = true;
                SetStatus($"Selected: {_selectedWindow.ProcessName}");
            }
        }

        private void BtnToggleVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_videoActive) StopVideo();
            else StartVideo();
        }

        private void BtnToggleAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_audioActive) StopAudio();
            else StartAudio();
        }

        private void CmbAudioOutput_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbAudioOutput.SelectedItem is not AudioOutputDevice device)
                return;

            _audioPlayer.SetOutputDevice(device.Id);
            SetStatus($"Audio output: {device.Name}");
        }

        private void BtnOpenStreamWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_streamWindow == null || !_streamWindow.IsLoaded)
            {
                _streamWindow = new TransmissionWindow();
                _streamWindow.Show();

                // If video is active, connect existing D3DImage
                if (_captureService.D3DDevice != null)
                {
                    // Will be populated on next frame arrival
                }
            }
            else
            {
                _streamWindow.Activate();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Video
        // ════════════════════════════════════════════════════════════════════

        private void StartVideo()
        {
            if (_selectedWindow == null) return;
            try
            {
                _captureService.StartCapture(_selectedWindow);
                _videoActive = true;
                BtnToggleVideo.Content = "Stop Video";
                BtnToggleVideo.Style = (Style)Resources["DangerButton"];
                VideoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x57, 0xF2, 0x87));
                TxtVideoStatus.Text = "Active";
                TxtVideoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x57, 0xF2, 0x87));
                SetStatus("Video capture started.");
            }
            catch (Exception ex)
            {
                SetStatus($"Video error: {ex.Message}");
                MessageBox.Show($"Could not start video capture:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopVideo()
        {
            _captureService.StopCapture();
            _videoActive = false;
            BtnToggleVideo.Content = "Start Video";
            BtnToggleVideo.Style = (Style)Resources["PrimaryButton"];
            VideoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x4A));
            TxtVideoStatus.Text = "Inactive";
            TxtVideoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0xAA));
            _streamWindow?.ShowNoSource();
            SetStatus("Video stopped.");
        }

        // ════════════════════════════════════════════════════════════════════
        // Frame pipeline: GPU texture → WriteableBitmap → UI
        //
        // NOTE: We use a WriteableBitmap (software readback) here for
        // maximum compatibility. For a pure GPU path, D3DImageSource
        // would be used but requires D3D9Ex availability.
        // The WriteableBitmap path adds ~1 frame of latency but avoids
        // driver compatibility issues.
        // ════════════════════════════════════════════════════════════════════

        private void OnFrameArrived(SharpDX.Direct3D11.Texture2D texture)
        {
            // Read back the GPU texture to CPU memory
            var desc = texture.Description;
            int width = desc.Width;
            int height = desc.Height;

            var stagingDesc = new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = SharpDX.Direct3D11.ResourceUsage.Staging,
                BindFlags = SharpDX.Direct3D11.BindFlags.None,
                CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read,
                OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
            };

            using var staging = new SharpDX.Direct3D11.Texture2D(
                _captureService.D3DDevice!, stagingDesc);

            _captureService.D3DDevice!.ImmediateContext.CopyResource(texture, staging);
            var mapped = _captureService.D3DDevice.ImmediateContext.MapSubresource(
                staging, 0, SharpDX.Direct3D11.MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);

            try
            {
                int stride = mapped.RowPitch;
                int byteCount = stride * height;
                byte[] pixelData = new byte[byteCount];
                System.Runtime.InteropServices.Marshal.Copy(mapped.DataPointer, pixelData, 0, byteCount);

                Dispatcher.InvokeAsync(() =>
                    PushFrameToUI(pixelData, width, height, stride),
                    DispatcherPriority.Render);
            }
            finally
            {
                _captureService.D3DDevice.ImmediateContext.UnmapSubresource(staging, 0);
                texture.Dispose();
            }
        }

        private void PushFrameToUI(byte[] pixels, int width, int height, int stride)
        {
            if (_previewBitmap == null ||
                _previewBitmap.PixelWidth != width ||
                _previewBitmap.PixelHeight != height)
            {
                _previewBitmap = new WriteableBitmap(
                    width, height, 96, 96, PixelFormats.Bgra32, null);
            }

            _previewBitmap.Lock();
            _previewBitmap.WritePixels(
                new Int32Rect(0, 0, width, height), pixels, stride, 0);
            _previewBitmap.Unlock();

            // Push to stream window
            if (_streamWindow != null && _streamWindow.IsLoaded)
            {
                _streamWindow.SetFrame(_previewBitmap);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Audio
        // ════════════════════════════════════════════════════════════════════

        private void StartAudio()
        {
            if (_selectedWindow == null) return;
            try
            {
                _audioCapture.StartCapture(_selectedWindow.ProcessId, includeProcessTree: true);
                _audioActive = true;
                BtnToggleAudio.Content = "Stop Audio";
                BtnToggleAudio.Style = (Style)Resources["DangerButton"];
                AudioStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x57, 0xF2, 0x87));
                TxtAudioStatus.Text = "Active";
                TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x57, 0xF2, 0x87));
                SetStatus("Audio capture started.");
            }
            catch (Exception ex)
            {
                SetStatus($"Audio error: {ex.Message}");
            }
        }

        private void StopAudio()
        {
            _audioCapture.StopCapture();
            _audioPlayer.Stop();
            _audioActive = false;
            BtnToggleAudio.Content = "Start Audio";
            BtnToggleAudio.Style = (Style)Resources["PrimaryButton"];
            AudioStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x4A));
            TxtAudioStatus.Text = "Inactive";
            TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0xAA));
            AudioLevelBar.Width = 0;
            SetStatus("Audio stopped.");
        }

        private void OnAudioData(byte[] data, int byteCount, Audio.WaveFormat format)
        {
            // Feed to player (background thread safe)
            _audioPlayer.Feed(data, byteCount, format);
        }

        private void OnAudioLevel(float level)
        {
            // Smooth and update meter on UI thread
            _smoothedLevel = _smoothedLevel * 0.7f + level * 0.3f;
            float clamped = Math.Min(_smoothedLevel * 3f, 1f); // amplify for display

            Dispatcher.InvokeAsync(() =>
            {
                double containerWidth = AudioLevelBar.ActualWidth > 0
                    ? AudioLevelBar.ActualWidth
                    : 400;
                AudioLevelBar.Width = clamped * containerWidth;
            }, DispatcherPriority.Background);
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private void LoadAudioOutputDevices()
        {
            try
            {
                var devices = new List<AudioOutputDevice>
                {
                    new(null, "Windows default output", isDefault: true)
                };

                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var endpoint in endpoints)
                {
                    devices.Add(new AudioOutputDevice(endpoint.ID, endpoint.FriendlyName));
                    endpoint.Dispose();
                }

                CmbAudioOutput.ItemsSource = devices;
                CmbAudioOutput.SelectedIndex = 0;
                _audioPlayer.SetOutputDevice(null);
            }
            catch (Exception ex)
            {
                CmbAudioOutput.ItemsSource = new[]
                {
                    new AudioOutputDevice(null, "Windows default output", isDefault: true)
                };
                CmbAudioOutput.SelectedIndex = 0;
                SetStatus($"Could not enumerate output devices: {ex.Message}");
            }
        }

        private void SetStatus(string msg)
        {
            TxtStatus.Text = msg;
            Console.WriteLine($"[StretchCord] {msg}");
        }

        protected override void OnClosed(EventArgs e)
        {
            StopVideo();
            StopAudio();
            _captureService.Dispose();
            _audioCapture.Dispose();
            _audioPlayer.Dispose();
            _streamWindow?.Close();
            base.OnClosed(e);
        }
    }
}
