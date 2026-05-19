using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StretchCord.UI
{
    /// <summary>
    /// The "stream window" – this is what users share in Discord.
    /// It shows the stretched video and plays StretchCord's audio.
    /// </summary>
    public partial class TransmissionWindow : Window
    {
        private bool _controlsVisible = true;
        private WindowState _prevWindowState;

        public TransmissionWindow()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
        }

        /// <summary>
        /// Update the displayed frame. Called from the capture pipeline on UI thread.
        /// </summary>
        public void SetFrame(BitmapSource bitmap)
        {
            PreviewImage.Source = bitmap;
            NoSourceLabel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Set a D3DImage as the source for GPU-direct rendering.
        /// </summary>
        public void SetD3DImageSource(System.Windows.Interop.D3DImage d3dImage)
        {
            PreviewImage.Source = d3dImage;
            NoSourceLabel.Visibility = Visibility.Collapsed;
        }

        public void ShowNoSource()
        {
            PreviewImage.Source = null;
            NoSourceLabel.Visibility = Visibility.Visible;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Enforce 16:9 when Shift is held — optional behavior
        }

        private void BtnToggleControls_Click(object sender, RoutedEventArgs e)
        {
            _controlsVisible = !_controlsVisible;
            OverlayPanel.Visibility = _controlsVisible ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleControls.Content = _controlsVisible ? "Hide Controls" : "Show Controls";
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                // Restore
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _prevWindowState;
                BtnFullscreen.Content = "⛶ Fullscreen";
            }
            else
            {
                _prevWindowState = WindowState;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                BtnFullscreen.Content = "⊡ Restore";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
