using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace StretchCord.Capture
{
    /// <summary>
    /// Helper to create GraphicsCaptureItem from an HWND via WinRT interop.
    /// Uses the IGraphicsCaptureItemInterop COM interface.
    /// </summary>
    public static class CaptureHelper
    {
        [ComImport]
        [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(
                IntPtr hwnd,
                [In] ref Guid riid);

            IntPtr CreateForMonitor(
                IntPtr hmon,
                [In] ref Guid riid);
        }

        private static readonly Guid IID_IGraphicsCaptureItem =
            new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = IID_IGraphicsCaptureItem;
            var itemPtr = interop.CreateForWindow(hwnd, ref iid);
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }

        public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = IID_IGraphicsCaptureItem;
            var itemPtr = interop.CreateForMonitor(hmon, ref iid);
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
    }
}
