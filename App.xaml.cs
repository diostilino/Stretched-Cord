using System.Windows;

namespace StretchCord
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize WinRT for this thread (needed for Windows.Graphics.Capture)
            global::Windows.System.DispatcherQueue.GetForCurrentThread();
        }
    }
}
