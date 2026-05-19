using System;

namespace StretchCord.Models
{
    /// <summary>
    /// Represents a capturable window with its associated process info.
    /// </summary>
    public class WindowInfo
    {
        public IntPtr Hwnd { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Title))
                return $"[{ProcessName}] (PID {ProcessId})";
            return $"{Title} — {ProcessName} (PID {ProcessId})";
        }
    }
}
