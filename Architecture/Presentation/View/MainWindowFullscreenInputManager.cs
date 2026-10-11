using System;
using System.Windows;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowFullscreenInputManager
    {
        private readonly MainWindow _window;

        public MainWindowFullscreenInputManager(MainWindow window)
        {
            _window = window;
        }

        public void FsPanel_MouseWheel(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            _window.AdjustVolumeByWheelFromManager(e.Delta);
            _window.ShowOverlayWithDelayFromManager();
        }
    }
}
