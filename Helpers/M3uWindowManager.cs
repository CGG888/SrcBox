using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace LibmpvIptvClient.Helpers
{
    public static class M3uWindowManager
    {
        private static M3uListWindow? _instance;
        private static DispatcherTimer? _timer;
        private static bool _autoFollow = true;
        static Rect GetAnchor()
        {
            var mw = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mw != null) return mw.GetVideoScreenRect();
            return new Rect(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }
        static void Center()
        {
            if (_instance == null) return;
            var r = GetAnchor();
            double w = _instance.ActualWidth > 1 ? _instance.ActualWidth : (_instance.Width > 1 ? _instance.Width : 640);
            double h = _instance.ActualHeight > 1 ? _instance.ActualHeight : (_instance.Height > 1 ? _instance.Height : 420);
            _instance.Left = r.Left + (r.Width - w) / 2;
            _instance.Top = r.Top + (r.Height - h) / 2;
        }
        public static void OpenOrActivate()
        {
            if (_instance != null && _instance.IsVisible)
            {
                Center();
                _instance.Topmost = OwnerWindowResolver.IsOwnerTopmost || _instance.Owner == null;
                _instance.Activate();
                return;
            }
            _instance = new M3uListWindow();
            bool isFs = false;
            try
            {
                // Fullscreen is applied to the main window itself, so the main window is also the owner to use.
                var mw = OwnerWindowResolver.MainWindow;
                if (mw != null) { _instance.Owner = mw; isFs = OwnerWindowResolver.IsOwnerTopmost; }
            }
            catch { }
            _instance.Loaded += (s, e) => Center();
            _instance.SizeChanged += (s, e) => { if (_autoFollow) Center(); };
            _instance.LocationChanged += (s, e) => { _autoFollow = false; };
            _instance.Closed += (s, e) =>
            {
                _instance = null; try { _timer?.Stop(); } catch { }
                try
                {
                    OwnerWindowResolver.MainWindow?.Activate();
                }
                catch { }
            };
            Center();
            _instance.Show();
            _instance.Topmost = isFs || _instance.Owner == null;
            try
            {
                var settings = System.Windows.Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault();
                if (settings != null && settings.IsVisible) settings.Activate();
            }
            catch { }
            _timer = _timer ?? new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(250) };
            _timer.Tick -= TimerTick;
            _timer.Tick += TimerTick;
            _autoFollow = true;
            _timer.Start();
        }
        static void TimerTick(object? sender, System.EventArgs e)
        {
            try
            {
                if (_instance == null || !_instance.IsVisible) return;
                if (_autoFollow) Center();
                _instance.Topmost = OwnerWindowResolver.IsOwnerTopmost || _instance.Owner == null;
            }
            catch { }
        }
    }
}
