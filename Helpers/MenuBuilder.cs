using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace LibmpvIptvClient.Helpers
{
    public static class MenuBuilder
    {
        public static ContextMenu BuildMainMenu(
            Action? openFile,
            Action? openUrl,
            Action? addM3uFile,
            Action? addM3uUrl,
            Action<M3uSource>? editM3u,
            Action<M3uSource>? loadM3u,
            Action? openSettings,
            Action? showAbout,
            Action? exitApp,
            Action<bool>? toggleFcc,
            Action<bool>? toggleUdp,
            Action<bool>? toggleEpg,
            Action<bool>? toggleDrawer,
            Action<bool>? toggleMinimal,
            Action<bool>? toggleDeinterlace,
            bool isEpgChecked,
            bool isDrawerChecked,
            bool isMinimalChecked)
        {
            var cm = new ContextMenu();

            // 1. File (Open)
            var miFile = new MenuItem { Header = Localizer.S("Menu_File", "打开") };
            var miOpenFile = new MenuItem { Header = Localizer.S("Menu_OpenFile", "打开文件...") };
            miOpenFile.Click += (s, args) => openFile?.Invoke();
            miFile.Items.Add(miOpenFile);

            var miOpenUrl = new MenuItem { Header = Localizer.S("Menu_OpenUrl", "打开链接...") };
            miOpenUrl.Click += (s, args) => openUrl?.Invoke();
            miFile.Items.Add(miOpenUrl);
            cm.Items.Add(miFile);

            // 2. M3U Management
            var miM3u = new MenuItem { Header = Localizer.S("Menu_M3U", "M3U") };
            var miAddFile = new MenuItem { Header = Localizer.S("Menu_AddM3uFile", "添加 M3U 文件...") };
            miAddFile.Click += (s, args) => addM3uFile?.Invoke();
            miM3u.Items.Add(miAddFile);

            var miAddUrl = new MenuItem { Header = Localizer.S("Menu_AddM3uUrl", "添加 M3U 地址...") };
            miAddUrl.Click += (s, args) => addM3uUrl?.Invoke();
            miM3u.Items.Add(miAddUrl);

            miM3u.Items.Add(new Separator());

            var miManage = new MenuItem { Header = Localizer.S("Menu_ManageM3u", "管理 M3U 列表") };
            miManage.Click += (s, a) =>
            {
                try
                {
                    LibmpvIptvClient.Helpers.M3uWindowManager.OpenOrActivate();
                }
                catch { }
            };
            miM3u.Items.Add(miManage);

            // 移除“编辑 M3U”子菜单，避免与“管理 M3U 列表”功能重复
            cm.Items.Add(miM3u);

            // 3. Performance Settings
            var miPerf = new MenuItem { Header = Localizer.S("Menu_Performance", "性能") };
            var miFcc = new MenuItem 
            { 
                Header = Localizer.S("Menu_FCC", "FCC (快速切台)"), 
                IsCheckable = true, 
                IsChecked = AppSettings.Current.FccPrefetchCount > 0 
            };
            miFcc.Click += (s, args) => toggleFcc?.Invoke(miFcc.IsChecked);
            miPerf.Items.Add(miFcc);

            var miUdp = new MenuItem
            {
                Header = Localizer.S("Menu_UDP", "UDP (组播优化)"),
                IsCheckable = true,
                IsChecked = AppSettings.Current.EnableUdpOptimization
            };
            miUdp.Click += (s, args) => toggleUdp?.Invoke(miUdp.IsChecked);
            miPerf.Items.Add(miUdp);

            // 反交错 (针对 1080i/720i 隔行扫描流)
            // 勾选状态: 当 Deinterlace != "no" 时视为开启
            var miDeinterlace = new MenuItem
            {
                Header = Localizer.S("Menu_Deinterlace", "反交错 (1080i/720i)"),
                IsCheckable = true,
                IsChecked = !string.Equals(AppSettings.Current.Deinterlace, "no", StringComparison.OrdinalIgnoreCase)
            };
            miDeinterlace.Click += (s, args) => toggleDeinterlace?.Invoke(miDeinterlace.IsChecked);
            miPerf.Items.Add(miDeinterlace);
            cm.Items.Add(miPerf);

            // 4. List Management
            var miList = new MenuItem { Header = Localizer.S("Menu_List", "列表") };
            var miEpg = new MenuItem 
            { 
                Header = Localizer.S("Menu_EPG", "EPG"), 
                IsCheckable = true, 
                IsChecked = isEpgChecked
            };
            miEpg.Click += (s, args) => toggleEpg?.Invoke(miEpg.IsChecked);
            miList.Items.Add(miEpg);

            var miChannel = new MenuItem 
            { 
                Header = Localizer.S("Menu_Channel", "频道"), 
                IsCheckable = true, 
                IsChecked = isDrawerChecked
            };
            miChannel.Click += (s, args) => toggleDrawer?.Invoke(miChannel.IsChecked);
            miList.Items.Add(miChannel);

            var miMinimal = new MenuItem
            {
                Header = Localizer.S("Menu_MinimalMode", "精简模式"),
                IsCheckable = true,
                IsChecked = isMinimalChecked
            };
            miMinimal.Click += (s, args) => toggleMinimal?.Invoke(miMinimal.IsChecked);
            miList.Items.Add(miMinimal);

            var miM3uList = new MenuItem { Header = Localizer.S("Menu_SwitchM3u", "切换 M3U") };
            if (AppSettings.Current.SavedSources != null && AppSettings.Current.SavedSources.Count > 0)
            {
                foreach (var src in AppSettings.Current.SavedSources)
                {
                    var miSrc = new MenuItem { Header = src.Name };
                    miSrc.Tag = src;
                    miSrc.IsCheckable = true;
                    miSrc.IsChecked = src.IsSelected;
                    miSrc.Click += (s, args) => 
                    {
                        if (s is MenuItem m && m.Tag is M3uSource source)
                            loadM3u?.Invoke(source);
                    };
                    miM3uList.Items.Add(miSrc);
                }
            }
            else
            {
                miM3uList.IsEnabled = false;
            }
            miList.Items.Add(miM3uList);
            cm.Items.Add(miList);

            var miReminder = new MenuItem { Header = Localizer.S("Menu_Reminders", "预约") };
            var miNotify = new MenuItem { Header = Localizer.S("Menu_ReminderNotify", "通知") };
            miNotify.Click += (s, a) =>
            {
                try
                {
                    LibmpvIptvClient.Helpers.ReminderWindowManager.OpenOrActivate();
                }
                catch { }
            };
            var miAutoplay = new MenuItem { Header = Localizer.S("Menu_ReminderAutoplay", "播放") };
            miAutoplay.Click += (s, a) =>
            {
                try
                {
                    LibmpvIptvClient.Helpers.ReminderWindowManager.OpenOrActivate();
                }
                catch { }
            };
            var miRecord = new MenuItem { Header = Localizer.S("Menu_ReminderRecord", "录播"), IsEnabled = false };
            miReminder.Items.Add(miNotify);
            miReminder.Items.Add(miAutoplay);
            miReminder.Items.Add(miRecord);
            cm.Items.Add(miReminder);

            var miSettings = new MenuItem { Header = Localizer.S("Menu_Settings", "设置") };
            miSettings.Click += (s, args) => openSettings?.Invoke();
            cm.Items.Add(new Separator());
            cm.Items.Add(miSettings);
            var miExit = new MenuItem { Header = Localizer.S("Menu_Exit", "退出") };
            miExit.Click += (s, args) => exitApp?.Invoke();
            cm.Items.Add(miExit);

            return cm;
        }
    }
}
