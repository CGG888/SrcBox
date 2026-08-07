using System;
using System.Windows;

namespace LibmpvIptvClient.Services
{
    public enum ToastKind
    {
        Appointment,
        ProgramStart,
        RecordStart,
        RecordStop,
        UploadSuccess,
        DownloadSuccess,
        DeleteLocal,
        DeleteRemote,
        DeleteBoth,
        UploadQueued
    }
    public class ToastService
    {
        public static void Init()
        {
            try { AudioManager.Instance.PreloadDefaults(); } catch { }
        }
        public static void ShowReminder(string channelId, string channelName, string program, DateTime startLocal, string? logoPath, bool isDue)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var owner = System.Windows.Application.Current?.Windows.OfType<FullscreenWindow>().FirstOrDefault(w => w.IsVisible)
                            as System.Windows.Window
                            ?? System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                var win = new ReminderToastWindow(channelId, channelName, program, startLocal, logoPath, isDue, null, null, false);
                try { if (owner != null) { win.Owner = owner; win.Topmost = true; } } catch { }
                ApplyKind(win, isDue ? ToastKind.ProgramStart : ToastKind.Appointment);
                win.Show();
            });
        }
        public static void ShowPlayAppointment(string channelId, string channelName, string program, DateTime startLocal, string? logoPath, string? playMode = null, int? countdownSec = null)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var owner = System.Windows.Application.Current?.Windows.OfType<FullscreenWindow>().FirstOrDefault(w => w.IsVisible)
                            as System.Windows.Window
                            ?? System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                var win = new ReminderToastWindow(channelId, channelName, program, startLocal, logoPath, true, null, null, true, playMode ?? "default", countdownSec);
                try { if (owner != null) { win.Owner = owner; win.Topmost = true; } } catch { }
                ApplyKind(win, ToastKind.ProgramStart, null);
                try { win.EnableAutoPlayManualClose(); } catch { }
                win.Show();
            });
        }
        public static void ShowRecordingAppointment(string channelId, string channelName, string program, DateTime startLocal, string? logoPath, string recordType, string? playMode = null, int? countdownSec = null)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var owner = System.Windows.Application.Current?.Windows.OfType<FullscreenWindow>().FirstOrDefault(w => w.IsVisible)
                            as System.Windows.Window
                            ?? System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                bool isRecordBack = string.Equals(recordType, "back", StringComparison.OrdinalIgnoreCase);
                var win = new ReminderToastWindow(channelId, channelName, program, startLocal, logoPath, true, null, null, true, playMode ?? "default", countdownSec, isRecordBack);
                try { if (owner != null) { win.Owner = owner; win.Topmost = true; } } catch { }
                ApplyKind(win, ToastKind.RecordStart, null);
                try { win.EnableAutoPlayManualClose(); } catch { }
                win.Show();
            });
        }
        public static void ShowSimple(ToastKind kind, string title, string? subtitle = null, string? logoPath = null, int? autoHideMs = null)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var now = DateTime.Now;
                string status = kind switch
                {
                    ToastKind.RecordStart => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Record_Start", "录播开始"),
                    ToastKind.RecordStop => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Record_Stop", "录播已停止"),
                    ToastKind.UploadSuccess => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Upload_Success", "上传成功"),
                    ToastKind.DownloadSuccess => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Download_Success", "下载成功"),
                    ToastKind.DeleteLocal => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Delete_Local", "本地删除"),
                    ToastKind.DeleteRemote => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Delete_Remote", "网络删除"),
                    ToastKind.DeleteBoth => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Delete_Both", "全部删除"),
                    ToastKind.UploadQueued => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Upload_Queued", "已加入上传"),
                    ToastKind.ProgramStart => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Status_Start", "节目开播"),
                    _ => LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Status_Booked", "预约成功")
                };
                var win = new ReminderToastWindow("", title ?? "SrcBox", subtitle ?? "", now, logoPath, false, null, status, false);
                ApplyKind(win, kind, autoHideMs);
                win.Show();
            });
        }
        static void ApplyKind(ReminderToastWindow w, ToastKind kind, int? autoHideMs = null)
        {
            try
            {
                switch (kind)
                {
                    case ToastKind.Appointment:
                        w.SetAutoHide((autoHideMs ?? 3000));
                        w.SetSound("appointment");
                        break;
                    case ToastKind.ProgramStart:
                        w.SetAutoHide((autoHideMs ?? 10000));
                        w.SetSound("program_start");
                        break;
                    case ToastKind.RecordStart:
                        w.SetAutoHide((autoHideMs ?? 3000));
                        w.SetSound("record_start");
                        break;
                    case ToastKind.RecordStop:
                        w.SetAutoHide((autoHideMs ?? 3000));
                        w.SetSound("record_stop");
                        break;
                    case ToastKind.UploadSuccess:
                        w.SetAutoHide((autoHideMs ?? 10000));
                        w.SetSound("upload_done");
                        break;
                    case ToastKind.DownloadSuccess:
                        w.SetAutoHide((autoHideMs ?? 10000));
                        w.SetSound("download_done");
                        break;
                    case ToastKind.DeleteLocal:
                    case ToastKind.DeleteRemote:
                    case ToastKind.DeleteBoth:
                        w.SetAutoHide((autoHideMs ?? 3000));
                        w.SetSound("record_stop");
                        break;
                    case ToastKind.UploadQueued:
                        w.SetAutoHide((autoHideMs ?? 3000));
                        w.SetSound("upload_queued");
                        break;
                }
            }
            catch { }
        }
    }
}
