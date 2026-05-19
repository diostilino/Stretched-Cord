using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace StretchCord.Capture
{
    /// <summary>
    /// Wraps a D3DImage and keeps it updated from a D3D11 texture on each frame.
    /// Must be used on the WPF UI thread for the D3DImage interaction.
    /// </summary>
    public sealed class D3DImageSource : IDisposable
    {
        private readonly D3DImage _d3dImage;
        private SharpDX.Direct3D9.Device? _d9Device;
        private SharpDX.Direct3D9.Texture? _sharedTexture9;
        private SharpDX.Direct3D11.Texture2D? _sharedTexture11;
        private SharpDX.Direct3D11.Device? _d3dDevice;
        private bool _disposed;

        public D3DImage D3DImage => _d3dImage;

        public D3DImageSource()
        {
            _d3dImage = new D3DImage();
        }

        public void Initialize(SharpDX.Direct3D11.Device d3dDevice)
        {
            _d3dDevice = d3dDevice;

            // Create a D3D9Ex device needed for D3DImage interop
            using var d3d9 = new SharpDX.Direct3D9.Direct3DEx();
            _d9Device = new SharpDX.Direct3D9.DeviceEx(
                d3d9,
                0,
                SharpDX.Direct3D9.DeviceType.Hardware,
                IntPtr.Zero,
                SharpDX.Direct3D9.CreateFlags.HardwareVertexProcessing |
                SharpDX.Direct3D9.CreateFlags.Multithreaded |
                SharpDX.Direct3D9.CreateFlags.FpuPreserve,
                new SharpDX.Direct3D9.PresentParameters
                {
                    Windowed = true,
                    SwapEffect = SharpDX.Direct3D9.SwapEffect.Discard,
                    DeviceWindowHandle = IntPtr.Zero,
                    PresentationInterval = SharpDX.Direct3D9.PresentInterval.Immediate
                });
        }

        /// <summary>
        /// Updates the D3DImage with the incoming D3D11 texture.
        /// Must be called on the UI thread, or dispatched.
        /// </summary>
        public void UpdateWith(SharpDX.Direct3D11.Texture2D sourceTex)
        {
            if (_disposed || _d3dDevice == null || _d9Device == null) return;

            var desc = sourceTex.Description;

            // Recreate shared texture if size changed
            if (_sharedTexture11 == null ||
                _sharedTexture11.Description.Width != desc.Width ||
                _sharedTexture11.Description.Height != desc.Height)
            {
                RecreateSharedTextures(desc.Width, desc.Height);
            }

            // Copy the capture frame into our shared texture
            _d3dDevice.ImmediateContext.CopyResource(sourceTex, _sharedTexture11!);
            _d3dDevice.ImmediateContext.Flush();

            // Get the shared handle for D3D9
            using var res = _sharedTexture11!.QueryInterface<SharpDX.DXGI.Resource>();
            IntPtr sharedHandle = res.SharedHandle;

            // Open the shared texture in D3D9
            _sharedTexture9?.Dispose();
            _sharedTexture9 = new SharpDX.Direct3D9.Texture(
                _d9Device,
                desc.Width, desc.Height, 1,
                SharpDX.Direct3D9.Usage.RenderTarget,
                SharpDX.Direct3D9.Format.A8R8G8B8,
                SharpDX.Direct3D9.Pool.Default,
                ref sharedHandle);

            using var surface9 = _sharedTexture9.GetSurfaceLevel(0);

            _d3dImage.Lock();
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface9.NativePointer);
            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, desc.Width, desc.Height));
            _d3dImage.Unlock();
        }

        private void RecreateSharedTextures(int width, int height)
        {
            _sharedTexture9?.Dispose();
            _sharedTexture11?.Dispose();

            _sharedTexture11 = new SharpDX.Direct3D11.Texture2D(_d3dDevice!, new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.Shared
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sharedTexture9?.Dispose();
            _sharedTexture11?.Dispose();
            _d9Device?.Dispose();
        }
    }
}
