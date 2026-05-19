using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StretchCord.Models
{
    /// <summary>
    /// Observable application state for data binding.
    /// </summary>
    public class AppState : INotifyPropertyChanged
    {
        private bool _videoActive;
        private bool _audioActive;
        private string _statusMessage = "Ready";
        private WindowInfo? _selectedWindow;
        private float _audioLevel;

        public bool VideoActive
        {
            get => _videoActive;
            set { _videoActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(VideoButtonLabel)); }
        }

        public bool AudioActive
        {
            get => _audioActive;
            set { _audioActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(AudioButtonLabel)); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public WindowInfo? SelectedWindow
        {
            get => _selectedWindow;
            set { _selectedWindow = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedWindowText)); }
        }

        public float AudioLevel
        {
            get => _audioLevel;
            set { _audioLevel = value; OnPropertyChanged(); }
        }

        public string SelectedWindowText =>
            _selectedWindow != null ? _selectedWindow.ToString() : "No window selected";

        public string VideoButtonLabel => _videoActive ? "Stop Video" : "Start Video";
        public string AudioButtonLabel => _audioActive ? "Stop Audio" : "Start Audio";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
