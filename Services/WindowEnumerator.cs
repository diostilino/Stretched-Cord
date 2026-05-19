using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using StretchCord.Models;

namespace StretchCord.Services
{
    /// <summary>
    /// Enumerates all visible, capturable top-level windows.
    /// </summary>
    public static class WindowEnumerator
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd); // minimized

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static List<WindowInfo> GetCapturableWindows()
        {
            var windows = new List<WindowInfo>();
            var shellWindow = GetShellWindow();

            EnumWindows((hwnd, _) =>
            {
                // Skip shell/desktop, invisible, minimized
                if (hwnd == shellWindow) return true;
                if (!IsWindowVisible(hwnd)) return true;
                if (IsIconic(hwnd)) return true;

                int len = GetWindowTextLength(hwnd);
                if (len == 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();

                // Skip windows with no meaningful title
                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowThreadProcessId(hwnd, out uint pid);

                string processName = "Unknown";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;

                    // Skip our own process
                    if (proc.ProcessName.Equals("StretchCord", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* process may have exited */ }

                windows.Add(new WindowInfo
                {
                    Hwnd = hwnd,
                    Title = title,
                    ProcessName = processName,
                    ProcessId = (int)pid
                });

                return true;
            }, IntPtr.Zero);

            return windows;
        }
    }
}
