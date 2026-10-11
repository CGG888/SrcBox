using System;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowWindowedInputManager
    {
        private readonly MainWindow _window;

        public MainWindowWindowedInputManager(MainWindow window)
        {
            _window = window;
        }

        public void MainPanel_DoubleClick(object? sender, EventArgs e)
        {
            _window.ToggleFullscreenSwitchFromManager();
        }

        public void VideoHost_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _window.AdjustVolumeByWheelFromManager(e.Delta);
            _window.ShowOverlayWithDelayFromManager();
            e.Handled = true;
        }

        public void VideoArea_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _window.AdjustVolumeByWheelFromManager(e.Delta);
            _window.ShowOverlayWithDelayFromManager();
            e.Handled = true;
        }

        public void BottomBar_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _window.AdjustVolumeByWheelFromManager(e.Delta);
            _window.ShowOverlayWithDelayFromManager();
            e.Handled = true;
        }
    }
}
