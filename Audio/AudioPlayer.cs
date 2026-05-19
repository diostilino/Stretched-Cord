using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StretchCord.Audio
{
    /// <summary>
    /// Plays process-loopback audio buffers through StretchCord itself.
    /// Discord can capture this app's audio when the StretchCord stream window
    /// is shared with sound enabled.
    /// </summary>
    public sealed class AudioPlayer : IDisposable
    {
        private WasapiOut? _wasapiOut;
        private BufferedWaveProvider? _buffer;
        private NAudio.Wave.WaveFormat? _currentFormat;
        private bool _disposed;
        private string? _outputDeviceId;
        private bool _outputDeviceChanged;
        private MMDevice? _activeOutputDevice;

        public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// Null means the Windows default render device.
        /// </summary>
        public string? OutputDeviceId => _outputDeviceId;

        public void SetOutputDevice(string? deviceId)
        {
            if (string.Equals(_outputDeviceId, deviceId, StringComparison.Ordinal))
                return;

            _outputDeviceId = deviceId;
            _outputDeviceChanged = true;

            // If playback is already running and we know the current format,
            // switch endpoints immediately instead of waiting for the next packet.
            if (_currentFormat != null && !_disposed)
                InitPlayer(_currentFormat);
        }

        /// <summary>
        /// Feed a captured audio buffer into the player.
        /// Called from the capture background thread.
        /// </summary>
        public void Feed(byte[] data, int byteCount, WaveFormat format)
        {
            if (_disposed) return;

            var naf = format.ToNAudio();

            // Re-initialize if format changed, endpoint changed, or player not started.
            if (_buffer == null ||
                _outputDeviceChanged ||
                !FormatEquals(naf, _currentFormat!) ||
                _wasapiOut?.PlaybackState != PlaybackState.Playing)
            {
                InitPlayer(naf);
            }

            if (_buffer!.BufferedBytes + byteCount > _buffer.BufferLength)
                _buffer.ClearBuffer();

            _buffer.AddSamples(data, 0, byteCount);
        }

        private void InitPlayer(NAudio.Wave.WaveFormat format)
        {
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _wasapiOut = null;
            _activeOutputDevice?.Dispose();
            _activeOutputDevice = null;

            _currentFormat = format;
            _buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true
            };

            using var enumerator = new MMDeviceEnumerator();

            if (!string.IsNullOrWhiteSpace(_outputDeviceId))
                _activeOutputDevice = enumerator.GetDevice(_outputDeviceId);

            // Explicit endpoint if selected; otherwise use Windows default render device.
            _wasapiOut = _activeOutputDevice != null
                ? new WasapiOut(_activeOutputDevice, AudioClientShareMode.Shared, true, 50)
                : new WasapiOut(AudioClientShareMode.Shared, 50);

            _wasapiOut.Init(_buffer);
            _wasapiOut.Play();
            _outputDeviceChanged = false;
        }

        private static bool FormatEquals(NAudio.Wave.WaveFormat a, NAudio.Wave.WaveFormat b)
            => a != null && b != null &&
               a.SampleRate == b.SampleRate &&
               a.Channels == b.Channels &&
               a.BitsPerSample == b.BitsPerSample;

        public void Stop()
        {
            _wasapiOut?.Stop();
            _buffer?.ClearBuffer();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _activeOutputDevice?.Dispose();
        }
    }
}
