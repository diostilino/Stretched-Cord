using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using StretchCord.Models;
using WinRT;

namespace StretchCord.Capture
{
    /// <summary>
    /// Captures a window using Windows.Graphics.Capture API (GPU path).
    /// Fires FrameArrived with a D3D11 texture on each new frame.
    /// </summary>
    public sealed class GraphicsCaptureService : IDisposable
    {
        // ── Events ──────────────────────────────────────────────────────────
        public event Action<SharpDX.Direct3D11.Texture2D>? FrameArrived;
        public event Action<string>? Error;

        // ── D3D ─────────────────────────────────────────────────────────────
        private SharpDX.Direct3D11.Device? _d3dDevice;
        private IDirect3DDevice? _wrtDevice;

        // ── WGC ─────────────────────────────────────────────────────────────
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;

        private SizeInt32 _lastSize;
        private bool _disposed;

        // ── Interop helpers ──────────────────────────────────────────────────
        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = true)]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter, int DriverType, IntPtr Software, uint Flags,
            IntPtr pFeatureLevels, uint FeatureLevels,
            uint SDKVersion, out IntPtr ppDevice,
            out int pFeatureLevel, out IntPtr ppImmediateContext);

        [ComImport]
        [System.Runtime.InteropServices.Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        // Creates a WinRT IDirect3DDevice wrapping a DXGI device.
        // This function is exported by d3d11.dll and returns an IUnknown ABI pointer.
        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        // ────────────────────────────────────────────────────────────────────

        public static bool IsSupported()
        {
            return ApiInformation.IsApiContractPresent(
                "Windows.Foundation.UniversalApiContract", 8);
        }

        public void Initialize()
        {
            // Create D3D11 device (hardware)
            const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
            int hr = D3D11CreateDevice(
                IntPtr.Zero, 1 /*D3D_DRIVER_TYPE_HARDWARE*/, IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, 7,
                out IntPtr devicePtr, out _, out _);

            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            _d3dDevice = new SharpDX.Direct3D11.Device(devicePtr);

            // Get DXGI device to wrap into WinRT IDirect3DDevice
            using var dxgiDevice = _d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
            int wrapHr = CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice.NativePointer,
                out IntPtr graphicsDevicePtr);

            if (wrapHr < 0)
                Marshal.ThrowExceptionForHR(wrapHr);

            try
            {
                _wrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePtr);
            }
            finally
            {
                if (graphicsDevicePtr != IntPtr.Zero)
                    Marshal.Release(graphicsDevicePtr);
            }
        }

        public void StartCapture(WindowInfo windowInfo)
        {
            StopCapture();

            if (_d3dDevice == null || _wrtDevice == null)
                throw new InvalidOperationException("Call Initialize() first.");

            // Create capture item from HWND
            _item = CaptureHelper.CreateItemForWindow(windowInfo.Hwnd);
            _lastSize = _item.Size;

            // Create frame pool
            _framePool = Direct3D11CaptureFramePool.Create(
                _wrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,          // number of buffered frames
                _lastSize);

            _session = _framePool.CreateCaptureSession(_item);

            // Hide the yellow capture border (Windows 11 22H2+, graceful fallback)
            try { _session.IsBorderRequired = false; } catch { }

            _framePool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
        }

        public void StopCapture()
        {
            _session?.Dispose();
            _session = null;

            if (_framePool != null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
                _framePool = null;
            }

            _item = null;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (_disposed) return;

            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            // Resize pool if the window changed size
            if (frame.ContentSize.Width != _lastSize.Width ||
                frame.ContentSize.Height != _lastSize.Height)
            {
                _lastSize = frame.ContentSize;
                _framePool?.Recreate(_wrtDevice!,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _lastSize);
            }

            try
            {
                // Get the underlying D3D texture from the WinRT surface.
                // Some C#/WinRT projections throw InvalidCastException when using surface.As<T>() here,
                // so use the lower-level IObjectReference path documented by CsWinRT.
                var surface = frame.Surface;
                var surfaceObjectRef = ((IWinRTObject)surface).NativeObject;
                var access = surfaceObjectRef.AsInterface<IDirect3DDxgiInterfaceAccess>();
                var iidTexture2D = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C"); // ID3D11Texture2D
                var texPtr = access.GetInterface(ref iidTexture2D);
                var texture = new SharpDX.Direct3D11.Texture2D(texPtr);

                FrameArrived?.Invoke(texture);
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Frame processing error: {ex.Message}");
            }
        }

        public SharpDX.Direct3D11.Device? D3DDevice => _d3dDevice;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCapture();
            _wrtDevice = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }
    }
}
