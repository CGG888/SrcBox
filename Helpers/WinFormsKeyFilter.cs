using System;
using System.Windows.Forms;

namespace LibmpvIptvClient
{
    internal class WinFormsKeyFilter : IMessageFilter
    {
        readonly Action _exit;
        readonly Action _playPause;
        readonly Action<int> _seek;
        public WinFormsKeyFilter(Action exit, Action playPause, Action<int> seek)
        {
            _exit = exit;
            _playPause = playPause;
            _seek = seek;
        }
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_SYSKEYDOWN = 0x0104;
            if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
            {
                var vk = (Keys)m.WParam.ToInt32();
                if (vk == Keys.Escape)
                {
                    _exit();
                    return true;
                }
                if (vk == Keys.Space)
                {
                    _playPause();
                    return true;
                }
                if (vk == Keys.Left)
                {
                    _seek(-1);
                    return true;
                }
                if (vk == Keys.Right)
                {
                    _seek(1);
                    return true;
                }
            }
            return false;
        }
    }
}
