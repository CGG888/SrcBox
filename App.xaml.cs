using System.Windows;
using System.Text;
using System;
using LibmpvIptvClient.Diagnostics;
using ModernWpf;
using System.Globalization;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using LibmpvIptvClient.Architecture.Core;

namespace LibmpvIptvClient
{
    public partial class App : System.Windows.Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        public static event Action? LanguageChanged;
        public static event Action? ThemeChanged;
        public App()
        {
            try
            {
                try
                {
                    var h = GetConsoleWindow();
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, SW_HIDE);
                        FreeConsole();
                    }
                }
                catch { }
                try
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var logDir = System.IO.Path.Combine(baseDir, "logs");
                    Directory.CreateDirectory(logDir);
                    var envLevel = Environment.GetEnvironmentVariable("SRCBOX_LOG_LEVEL");
                    if (!string.IsNullOrWhiteSpace(envLevel))
                    {
                        try
                        {
                            if (Enum.TryParse<LibmpvIptvClient.Diagnostics.LogLevel>(envLevel, true, out var lvl))
                                LibmpvIptvClient.Diagnostics.Logger.MinimumLevel = lvl;
                        }
                        catch { }
                    }
                    CleanupOldLogs(logDir, 7);
                    LibmpvIptvClient.Diagnostics.Logger.OnMessageLeveled += (level, msg) =>
                    {
                        try
                        {
                            var day = DateTime.Now.ToString("yyyyMMdd");
                            var file = System.IO.Path.Combine(logDir, $"SrcBox-{day}.log");
                            File.AppendAllText(file, msg + Environment.NewLine);
                            var fileByLevel = System.IO.Path.Combine(logDir, $"SrcBox-{level}-{day}.log");
                            File.AppendAllText(fileByLevel, msg + Environment.NewLine);
                        }
                        catch { }
                    };
                }
                catch { }
            }
            catch { }

            this.DispatcherUnhandledException += (s, e) =>
            {
                try { Logger.Fatal("未处理异常 " + e.Exception?.ToString()); } catch { }
                System.Windows.MessageBox.Show(e.Exception?.Message ?? LibmpvIptvClient.Helpers.ResxLocalizer.Get("Err_Unknown", "未知错误"),
                    LibmpvIptvClient.Helpers.ResxLocalizer.Get("Err_UnhandledTitle", "错误"), MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { Logger.Fatal("未处理异常 " + e.ExceptionObject?.ToString()); } catch { }
                System.Windows.MessageBox.Show(e.ExceptionObject?.ToString() ?? LibmpvIptvClient.Helpers.ResxLocalizer.Get("Err_Unknown", "未知错误"),
                    LibmpvIptvClient.Helpers.ResxLocalizer.Get("Err_UnhandledTitle", "错误"), MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
        static void CleanupOldLogs(string dir, int keepDays)
        {
            try
            {
                var files = Directory.GetFiles(dir, "SrcBox-*.log", SearchOption.TopDirectoryOnly);
                var cutoff = DateTime.Today.AddDays(-keepDays + 1);
                foreach (var f in files)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d{8})$");
                        if (m.Success)
                        {
                            if (DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                            {
                                if (dt < cutoff) File.Delete(f);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        static bool IsTestEnvironment()
        {
            try
            {
                var pn = Process.GetCurrentProcess()?.ProcessName ?? "";
                var fn = AppDomain.CurrentDomain?.FriendlyName ?? "";
                var args = Environment.GetCommandLineArgs() ?? Array.Empty<string>();
                if (pn.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (fn.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (args.Any(a => a.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("vstest", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }
            catch { }
            return false;
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch { }
            try
            {
                try { LibmpvIptvClient.Services.ToastService.Init(); } catch { }
                try { SrcBoxArchitectureHost.InitializeAsync().GetAwaiter().GetResult(); } catch { }
                ApplyTheme(AppSettings.Current.ThemeMode);
                ApplyLanguage(AppSettings.Current.Language);
            }
            catch { }
        }
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                LibmpvIptvClient.Services.WebRemote.WebRemoteManager.Shutdown();
            }
            catch { }
            try
            {
                SrcBoxArchitectureHost.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch { }
            base.OnExit(e);
        }
        public static void ApplyTheme(string mode)
        {
            try
            {
                if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                else if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                else
                    ThemeManager.Current.ApplicationTheme = null;
                var app = System.Windows.Application.Current;
                if (app == null) return;
                var remove = new System.Collections.Generic.List<ResourceDictionary>();
                foreach (var rd in app.Resources.MergedDictionaries)
                {
                    if (rd.Source != null && rd.Source.OriginalString.StartsWith("Resources/Colors.", StringComparison.OrdinalIgnoreCase))
                        remove.Add(rd);
                }
                foreach (var r in remove) app.Resources.MergedDictionaries.Remove(r);
                var actual = ThemeManager.Current.ActualApplicationTheme;
                var src = actual == ApplicationTheme.Light ? "Resources/Colors.Light.xaml" : "Resources/Colors.Dark.xaml";
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(src, UriKind.Relative) });
                foreach (Window w in app.Windows)
                {
                    try
                    {
                        LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(w);
                        w.InvalidateVisual();
                        w.UpdateLayout();
                    }
                    catch { }
                }
                ThemeChanged?.Invoke();
            }
            catch { }
        }
        public static void ApplyLanguage(string lang)
        {
            try
            {
                CultureInfo ci = string.IsNullOrWhiteSpace(lang) ? CultureInfo.InstalledUICulture : new CultureInfo(lang);
                Thread.CurrentThread.CurrentUICulture = ci;
                Thread.CurrentThread.CurrentCulture = ci;
                LibmpvIptvClient.Helpers.ResxLocalizer.SetCulture(ci);
                var app = System.Windows.Application.Current;
                if (app == null) return;
                var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
                foreach (var rd in app.Resources.MergedDictionaries)
                {
                    try
                    {
                        if (rd.Source != null && rd.Source.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase))
                            toRemove.Add(rd);
                    }
                    catch { }
                }
                foreach (var r in toRemove) app.Resources.MergedDictionaries.Remove(r);
                string code;
                if (ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    code = ci.Name.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 ? "zh-TW" : "zh-CN";
                else if (ci.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                    code = "ru-RU";
                else
                    code = "en-US";
                var dict = new ResourceDictionary { Source = new Uri($"Resources/Strings.{code}.xaml", UriKind.Relative) };
                app.Resources.MergedDictionaries.Add(dict);
                LanguageChanged?.Invoke();
            }
            catch { }
        }
    }
}
