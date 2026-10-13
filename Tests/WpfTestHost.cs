using System;
using System.Threading;
using System.Windows.Threading;

namespace LibmpvIptvClient.Tests
{
    internal static class WpfTestHost
    {
        static readonly ManualResetEventSlim Ready = new(false);
        static Dispatcher? _dispatcher;
        static Thread? _thread;

        static Dispatcher Dispatcher
        {
            get
            {
                EnsureStarted();
                return _dispatcher!;
            }
        }

        static void EnsureStarted()
        {
            if (_dispatcher != null) return;
            lock (Ready)
            {
                if (_dispatcher != null) return;
                _thread = new Thread(() =>
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    Ready.Set();
                    Dispatcher.Run();
                });
                _thread.IsBackground = true;
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                Ready.Wait(TimeSpan.FromSeconds(5));
            }
        }

        public static void Invoke(Action action)
        {
            Exception? error = null;
            Dispatcher.Invoke(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            if (error != null) throw error;
        }

        static bool _appResources;

        /// <summary>
        /// Creates the headless <see cref="System.Windows.Application"/> of this host and merges the app-level style
        /// dictionary. Controls that pull a style with {StaticResource} need it: both seek bars use
        /// UnifiedThickSlider, which lives in Styles/PlayerStyles.xaml. Call it on the host dispatcher, before
        /// constructing such a control.
        /// </summary>
        public static void EnsureAppResources()
        {
            if (_appResources) return;
            var app = System.Windows.Application.Current
                ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SrcBox;component/Styles/PlayerStyles.xaml", UriKind.Absolute)
            });
            _appResources = true;
        }
    }
}
