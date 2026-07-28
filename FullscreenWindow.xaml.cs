using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Input;

namespace LibmpvIptvClient
{
    public partial class FullscreenWindow : Window
    {
        public Panel VideoPanel => FsPanel;
        public WindowsFormsHost Host => FsHost;
        public event System.Action? ExitRequested;
        public event System.Action? PlayPauseRequested;
        public event System.Action<int>? SeekRequested; // -1 left, +1 right
        public event System.Func<Key, bool>? ShortcutKeyPressed;
        public FullscreenWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInit;
        }
        HwndSourceHook? _hook;
        void OnSourceInit(object? sender, System.EventArgs e)
        {
            var src = (HwndSource)PresentationSource.FromVisual(this);
            _hook = new HwndSourceHook(WndProc);
            src.AddHook(_hook);
        }
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_SYSKEYDOWN = 0x0104;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vk = wParam.ToInt32();
                // 只处理 Space，其他所有键都让 OnKeyDown 处理
                if (vk == 0x20)
                {
                    PlayPauseRequested?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
        void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 所有快捷键都通过 ShortcutKeyPressed 处理
            bool handledByShortcut = ShortcutKeyPressed?.Invoke(e.Key) ?? false;
            if (handledByShortcut)
            {
                e.Handled = true;
                return;
            }

            // 未被快捷键系统处理的特殊键
            switch (e.Key)
            {
                case Key.Escape:
                    ExitRequested?.Invoke();
                    e.Handled = true;
                    break;
                case Key.Space:
                    PlayPauseRequested?.Invoke();
                    e.Handled = true;
                    break;
                // Left/Right 由快捷键系统处理（切换源）
                // 不再默认处理快进快退
            }
        }
    }
}
