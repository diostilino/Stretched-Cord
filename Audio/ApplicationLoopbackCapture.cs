using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StretchCord.Audio
{
    /// <summary>
    /// Captures only the selected process audio using Windows process-loopback activation.
    /// This intentionally does NOT fall back to full system loopback: system-loopback + playback
    /// creates an infinite feedback loop when Discord captures StretchCord's own output.
    /// </summary>
    public sealed class ApplicationLoopbackCapture : IDisposable
    {
        public event Action<byte[], int, WaveFormat>? AudioDataAvailable;
        public event Action<float>? LevelChanged;
        public event Action<string>? Error;

        private Thread? _captureThread;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public bool IsCapturing => _captureThread?.IsAlive == true;

        private const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";
        private const ushort VT_BLOB = 65;
        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000);
        private const int CLSCTX_ALL = 23;
        private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        public void StartCapture(int processId, bool includeProcessTree = true)
        {
            if (_captureThread?.IsAlive == true) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _captureThread = new Thread(() => CaptureLoop(processId, includeProcessTree, token))
            {
                IsBackground = true,
                Name = "ProcessLoopbackAudioCapture",
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.SetApartmentState(ApartmentState.MTA);
            _captureThread.Start();
        }

        public void StopCapture()
        {
            _cts?.Cancel();
            _captureThread?.Join(3000);
            _captureThread = null;
        }

        private void CaptureLoop(int processId, bool includeTree, CancellationToken token)
        {
            int coInit = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            bool comInitialized = coInit >= 0 || coInit == RPC_E_CHANGED_MODE;

            try
            {
                if (!TryActivateProcessLoopback(processId, includeTree, out var audioClient, out var failure))
                {
                    Error?.Invoke(failure ?? "Could not start process-loopback audio capture.");
                    return;
                }

                if (audioClient == null)
                {
                    Error?.Invoke("Process-loopback activation returned no audio client.");
                    return;
                }

                try
                {
                    var format = WaveFormat.CreatePcm16Stereo44100();
                    var wfx = format.ToWaveFormatEx();

                    int hr = audioClient.Initialize(
                        AUDCLNT_SHAREMODE_SHARED,
                        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                        0,
                        0,
                        ref wfx,
                        Guid.Empty);
                    if (hr < 0)
                    {
                        Error?.Invoke($"Process-loopback IAudioClient.Initialize failed: 0x{hr:X8}");
                        return;
                    }

                    hr = audioClient.GetService(IID_IAudioCaptureClient, out object captureObject);
                    if (hr < 0 || captureObject is not IAudioCaptureClient captureClient)
                    {
                        Error?.Invoke($"Process-loopback GetService(IAudioCaptureClient) failed: 0x{hr:X8}");
                        return;
                    }

                    hr = audioClient.Start();
                    if (hr < 0)
                    {
                        Error?.Invoke($"Process-loopback IAudioClient.Start failed: 0x{hr:X8}");
                        return;
                    }

                    try
                    {
                        PollLoop(captureClient, format, token);
                    }
                    finally
                    {
                        try { audioClient.Stop(); } catch { }
                    }
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(audioClient); } catch { }
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Process-loopback audio capture failed: {ex.Message}");
            }
            finally
            {
                if (comInitialized && coInit != RPC_E_CHANGED_MODE)
                    CoUninitialize();
            }
        }

        private bool TryActivateProcessLoopback(
            int processId,
            bool includeTree,
            out IAudioClient? audioClient,
            out string? failure)
        {
            audioClient = null;
            failure = null;

            var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.PROCESS_LOOPBACK,
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = unchecked((uint)processId),
                    ProcessLoopbackMode = includeTree
                        ? PROCESS_LOOPBACK_MODE.INCLUDE_TARGET_PROCESS_TREE
                        : PROCESS_LOOPBACK_MODE.EXCLUDE_TARGET_PROCESS_TREE
                }
            };

            IntPtr activationParamsPtr = IntPtr.Zero;
            IActivateAudioInterfaceAsyncOperation? asyncOperation = null;
            var completionHandler = new ActivationCompletionHandler();

            try
            {
                activationParamsPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>());
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);

                var propVariant = new PROPVARIANT
                {
                    vt = VT_BLOB,
                    blob = new BLOB
                    {
                        cbSize = unchecked((uint)Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>()),
                        pBlobData = activationParamsPtr
                    }
                };

                Guid iid = IID_IAudioClient;
                int hr = ActivateAudioInterfaceAsync(
                    VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                    ref iid,
                    ref propVariant,
                    completionHandler,
                    out asyncOperation);

                if (hr < 0)
                {
                    failure = $"ActivateAudioInterfaceAsync failed: 0x{hr:X8}";
                    return false;
                }

                if (!completionHandler.Wait(TimeSpan.FromSeconds(8)))
                {
                    failure = "Process-loopback activation timed out.";
                    return false;
                }

                if (completionHandler.CallbackHResult < 0)
                {
                    failure = $"Process-loopback activation callback failed: 0x{completionHandler.CallbackHResult:X8}";
                    return false;
                }

                if (completionHandler.ActivationHResult < 0)
                {
                    failure = $"Process-loopback activation failed: 0x{completionHandler.ActivationHResult:X8}";
                    return false;
                }

                if (completionHandler.ActivatedInterface is not IAudioClient client)
                {
                    failure = "Process-loopback activation did not return an IAudioClient.";
                    return false;
                }

                audioClient = client;
                return true;
            }
            finally
            {
                completionHandler.Dispose();
                if (asyncOperation != null)
                {
                    try { Marshal.FinalReleaseComObject(asyncOperation); } catch { }
                }

                if (activationParamsPtr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(activationParamsPtr);
            }
        }

        private void PollLoop(IAudioCaptureClient capture, WaveFormat fmt, CancellationToken token)
        {
            var buffer = new byte[Math.Max(fmt.SampleRate * fmt.BlockAlign, 4096)];

            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(6);

                int hr = capture.GetNextPacketSize(out uint packetSize);
                if (hr < 0)
                {
                    Error?.Invoke($"Audio GetNextPacketSize failed: 0x{hr:X8}");
                    return;
                }

                while (packetSize > 0 && !token.IsCancellationRequested)
                {
                    hr = capture.GetBuffer(out IntPtr dataPtr, out uint numFrames, out uint flags, out _, out _);
                    if (hr < 0)
                    {
                        Error?.Invoke($"Audio GetBuffer failed: 0x{hr:X8}");
                        return;
                    }

                    int byteCount = checked((int)(numFrames * (uint)fmt.BlockAlign));
                    try
                    {
                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && byteCount > 0)
                        {
                            if (byteCount > buffer.Length)
                                buffer = new byte[byteCount * 2];

                            Marshal.Copy(dataPtr, buffer, 0, byteCount);
                            var copy = new byte[byteCount];
                            Buffer.BlockCopy(buffer, 0, copy, 0, byteCount);
                            LevelChanged?.Invoke(ComputeRmsPcm16(copy, byteCount));
                            AudioDataAvailable?.Invoke(copy, byteCount, fmt);
                        }
                    }
                    finally
                    {
                        capture.ReleaseBuffer(numFrames);
                    }

                    hr = capture.GetNextPacketSize(out packetSize);
                    if (hr < 0)
                    {
                        Error?.Invoke($"Audio GetNextPacketSize failed: 0x{hr:X8}");
                        return;
                    }
                }
            }
        }

        private static float ComputeRmsPcm16(byte[] data, int byteCount)
        {
            int sampleCount = byteCount / 2;
            if (sampleCount <= 0) return 0f;

            double sum = 0;
            for (int i = 0; i + 1 < byteCount; i += 2)
            {
                short sample = BitConverter.ToInt16(data, i);
                double normalized = sample / 32768.0;
                sum += normalized * normalized;
            }

            return (float)Math.Sqrt(sum / sampleCount);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCapture();
        }

        private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IDisposable
        {
            private readonly ManualResetEventSlim _completed = new(false);

            public int CallbackHResult { get; private set; }
            public int ActivationHResult { get; private set; } = unchecked((int)0x8000FFFF);
            public object? ActivatedInterface { get; private set; }

            public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                try
                {
                    CallbackHResult = operation.GetActivateResult(out int activationHr, out object activatedInterface);
                    ActivationHResult = activationHr;
                    ActivatedInterface = activatedInterface;
                    return 0;
                }
                catch (Exception ex)
                {
                    CallbackHResult = Marshal.GetHRForException(ex);
                    return CallbackHResult;
                }
                finally
                {
                    _completed.Set();
                }
            }

            public bool Wait(TimeSpan timeout) => _completed.Wait(timeout);

            public void Dispose() => _completed.Dispose();
        }

        [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid,
            ref PROPVARIANT activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint COINIT_MULTITHREADED = 0x0;
        private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        private enum AUDIOCLIENT_ACTIVATION_TYPE : uint
        {
            DEFAULT = 0,
            PROCESS_LOOPBACK = 1
        }

        private enum PROCESS_LOOPBACK_MODE : uint
        {
            INCLUDE_TARGET_PROCESS_TREE = 0,
            EXCLUDE_TARGET_PROCESS_TREE = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
        {
            public uint TargetProcessId;
            public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_ACTIVATION_PARAMS
        {
            public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
            public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLOB
        {
            public uint cbSize;
            public IntPtr pBlobData;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public BLOB blob;
        }

        [ComImport]
        [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig]
            int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport]
        [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig]
            int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport]
        [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, ref WaveFormatEx pFormat, Guid audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
            [PreserveSig] int GetStreamLatency(out long phnsLatency);
            [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
            [PreserveSig] int IsFormatSupported(int shareMode, ref WaveFormatEx pFormat, out IntPtr ppClosestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
            [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport]
        [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
            [PreserveSig] int ReleaseBuffer(uint numFramesRead);
            [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }
    }

    public enum WaveFormatEncoding : ushort { Pcm = 1, IeeeFloat = 3, Extensible = 0xFFFE }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    public class WaveFormat
    {
        public WaveFormatEncoding Encoding { get; set; }
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public int BlockAlign => Channels * BitsPerSample / 8;

        public static WaveFormat CreatePcm16Stereo44100() => new()
        {
            Encoding = WaveFormatEncoding.Pcm,
            Channels = 2,
            SampleRate = 44100,
            BitsPerSample = 16
        };

        public WaveFormatEx ToWaveFormatEx() => new()
        {
            wFormatTag = (ushort)Encoding,
            nChannels = (ushort)Channels,
            nSamplesPerSec = (uint)SampleRate,
            nAvgBytesPerSec = (uint)(SampleRate * BlockAlign),
            nBlockAlign = (ushort)BlockAlign,
            wBitsPerSample = (ushort)BitsPerSample,
            cbSize = 0
        };

        public NAudio.Wave.WaveFormat ToNAudio() =>
            (BitsPerSample == 32 && Encoding == WaveFormatEncoding.IeeeFloat)
                ? NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)
                : new NAudio.Wave.WaveFormat(SampleRate, BitsPerSample, Channels);
    }
}
