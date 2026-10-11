using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Always-on-top badge that flashes the channel number in the top-right corner of the player video
    /// area. Only channel switches coming from the web remote show it.
    /// </summary>
    public partial class ChannelNumberOverlay : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        public ChannelNumberOverlay()
        {
            InitializeComponent();
        }

        /// <summary>Sets the number shown by the badge.</summary>
        public void SetNumber(int number)
        {
            TbNumber.Text = number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Makes the badge click-through and keeps it out of the activation order, so the video
        /// area behind it stays clickable while the badge is visible.</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                var style = GetWindowLong(handle, GwlExStyle);
                SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
            }
            catch
            {
                // The badge is decoration only; a failed style tweak must never break playback.
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
