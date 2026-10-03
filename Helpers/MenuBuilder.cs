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
            bool isMinimalChecked,
            bool minimalMode = false,
            Action? refreshChannels = null,
            Action? togglePlayPause = null,
            Action? stopPlayback = null,
            Action? seekForward = null,
            Action? seekBackward = null,
            Action? prevChannel = null,
            Action? nextChannel = null,
            Action? toggleMute = null,
            Action? volumeUp = null,
            Action? volumeDown = null,
            Action<bool>? toggleTopmost = null,
            Action? openDebug = null,
            Action? showShortcuts = null,
            bool isTopmostChecked = false,
            Action? clearCloseMode = null,
            Action<string>? decoderChanged = null,
            Action<int>? openMultiScreen = null)
        {
            var cm = new ContextMenu();

            // 1. 打开 (File)
            var miFile = new MenuItem { Header = Localizer.S("Menu_File", "打开") };
            var miOpenFile = new MenuItem { Header = Localizer.S("Menu_OpenFile", "打开文件..."), InputGestureText = "Ctrl+O" };
            Wire(miOpenFile, openFile);
            miFile.Items.Add(miOpenFile);

            var miOpenUrl = new MenuItem { Header = Localizer.S("Menu_OpenUrl", "打开链接..."), InputGestureText = "Ctrl+U" };
            Wire(miOpenUrl, openUrl);
            miFile.Items.Add(miOpenUrl);
            cm.Items.Add(miFile);

            // 2. 频道 (M3U Management)
            var miChannel = new MenuItem { Header = Localizer.S("Menu_Channel", "频道") };
            var miAddFile = new MenuItem { Header = Localizer.S("Menu_AddM3uFile", "添加文件"), InputGestureText = "Ctrl+N" };
            Wire(miAddFile, addM3uFile);
            miChannel.Items.Add(miAddFile);

            var miAddUrl = new MenuItem { Header = Localizer.S("Menu_AddM3uUrl", "添加链接"), InputGestureText = "Ctrl+B" };
            Wire(miAddUrl, addM3uUrl);
            miChannel.Items.Add(miAddUrl);

            miChannel.Items.Add(new Separator());

            var miManage = new MenuItem { Header = Localizer.S("Menu_ManageM3u", "管理频道数据"), InputGestureText = "Ctrl+M" };
            miManage.Click += (s, a) =>
            {
                try
                {
                    LibmpvIptvClient.Helpers.M3uWindowManager.OpenOrActivate();
                }
                catch { }
            };
            miChannel.Items.Add(miManage);

            var miRefresh = new MenuItem { Header = Localizer.S("Menu_RefreshChannels", "刷新频道"), InputGestureText = "F5" };
            Wire(miRefresh, refreshChannels);
            miChannel.Items.Add(miRefresh);

            miChannel.Items.Add(new Separator());

            var miM3uList = new MenuItem { Header = Localizer.S("Menu_SwitchM3u", "切换频道数据") };
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
            miChannel.Items.Add(miM3uList);

            var miEpg = new MenuItem
            {
                Header = Localizer.S("Menu_EPG", "节目指南"),
                InputGestureText = "E",
                IsCheckable = true,
                IsChecked = isEpgChecked,
                // Minimal mode only shows the playback area, so the panel toggles are unavailable.
                IsEnabled = LibmpvIptvClient.Helpers.PanelWindowLayout.CanOpenPanels(minimalMode)
            };
            Wire(miEpg, toggleEpg);
            miChannel.Items.Add(miEpg);

            var miDrawer = new MenuItem
            {
                Header = Localizer.S("Menu_Drawer", "频道列表"),
                InputGestureText = "L",
                IsCheckable = true,
                IsChecked = isDrawerChecked,
                IsEnabled = LibmpvIptvClient.Helpers.PanelWindowLayout.CanOpenPanels(minimalMode)
            };
            Wire(miDrawer, toggleDrawer);
            miChannel.Items.Add(miDrawer);

            cm.Items.Add(miChannel);

            // 3. 播放
            var miPlay = new MenuItem { Header = Localizer.S("Menu_Playback", "播放") };

            var miControl = new MenuItem { Header = Localizer.S("Menu_Control", "控制") };
            var miPlayPause = new MenuItem { Header = Localizer.S("Menu_PlayPause", "播放/暂停"), InputGestureText = "Space" };
            Wire(miPlayPause, togglePlayPause);
            miControl.Items.Add(miPlayPause);

            var miStop = new MenuItem { Header = Localizer.S("Menu_Stop", "停止"), InputGestureText = "S" };
            Wire(miStop, stopPlayback);
            miControl.Items.Add(miStop);

            miControl.Items.Add(new Separator());

            var miFastForward = new MenuItem { Header = Localizer.S("Menu_FastForward", "快进"), InputGestureText = "→" };
            Wire(miFastForward, seekForward);
            miControl.Items.Add(miFastForward);

            var miRewind = new MenuItem { Header = Localizer.S("Menu_Rewind", "快退"), InputGestureText = "←" };
            Wire(miRewind, seekBackward);
            miControl.Items.Add(miRewind);

            miControl.Items.Add(new Separator());

            var miPrevCh = new MenuItem { Header = Localizer.S("Menu_PrevChannel", "上一频道"), InputGestureText = "↑" };
            Wire(miPrevCh, prevChannel);
            miControl.Items.Add(miPrevCh);

            var miNextCh = new MenuItem { Header = Localizer.S("Menu_NextChannel", "下一频道"), InputGestureText = "↓" };
            Wire(miNextCh, nextChannel);
            miControl.Items.Add(miNextCh);

            miControl.Items.Add(new Separator());

            var miSwitchSource = new MenuItem { Header = Localizer.S("Menu_SwitchSource", "切换源"), InputGestureText = "←/→" };
            Wire(miSwitchSource, _switchSourceCallback);
            miControl.Items.Add(miSwitchSource);

            miPlay.Items.Add(miControl);

            if (openMultiScreen != null)
            {
                var miMultiScreen = new MenuItem { Header = Localizer.S("Menu_MultiScreen", "多屏播放") };
                var mi4Screen = new MenuItem { Header = Localizer.S("Menu_4Screen", "4 屏 (2×2)"), InputGestureText = "Ctrl+4" };
                mi4Screen.Click += (s, args) => openMultiScreen(4);
                miMultiScreen.Items.Add(mi4Screen);
                var mi6Screen = new MenuItem { Header = Localizer.S("Menu_6Screen", "6 屏 (2×3)"), InputGestureText = "Ctrl+6" };
                mi6Screen.Click += (s, args) => openMultiScreen(6);
                miMultiScreen.Items.Add(mi6Screen);
                var mi9Screen = new MenuItem { Header = Localizer.S("Menu_9Screen", "9 屏 (3×3)"), InputGestureText = "Ctrl+9" };
                mi9Screen.Click += (s, args) => openMultiScreen(9);
                miMultiScreen.Items.Add(mi9Screen);
                miPlay.Items.Add(miMultiScreen);
            }

            var miSpeed = new MenuItem { Header = Localizer.S("Menu_Speed", "播放速度") };
            var speeds = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 5.0 };
            foreach (var sp in speeds)
            {
                var miSp = new MenuItem { Header = $"{sp:0.##}x", Tag = sp, IsCheckable = true, IsChecked = Math.Abs(sp - _currentSpeed) < 0.001 };
                if (_speedCallback == null) miSp.IsEnabled = false;   // no handler bound (e.g. the title bar menu)
                miSp.Click += (s, args) =>
                {
                    _currentSpeed = sp;
                    try { _speedCallback?.Invoke(sp); } catch { }
                };
                miSpeed.Items.Add(miSp);
                _speedMenuItems.Add(miSp);
            }
            miPlay.Items.Add(miSpeed);
            RegisterSpeedMenu(cm);

            cm.Items.Add(miPlay);

            // [分隔线] 播放 ↔ 视频
            cm.Items.Add(new Separator());

            // 4. 视频
            var miVideo = new MenuItem { Header = Localizer.S("Menu_Video", "视频") };

            var miRatio = new MenuItem { Header = Localizer.S("Menu_AspectRatio", "画面比例") };
            var ratios = new[] {
                (Localizer.S("Ratio_Default", "默认"), "default"),
                ("16:9", "16:9"),
                ("4:3", "4:3"),
                (Localizer.S("Ratio_Stretch", "拉伸"), "stretch"),
                (Localizer.S("Ratio_Fill", "填充"), "fill"),
                (Localizer.S("Ratio_Crop", "裁剪"), "crop")
            };
            foreach (var (label, val) in ratios)
            {
                var miRatioItem = new MenuItem { Header = label, Tag = val, IsCheckable = true, IsChecked = string.Equals(val, _currentAspectRatio, StringComparison.OrdinalIgnoreCase) };
                if (_ratioCallback == null) miRatioItem.IsEnabled = false;   // no handler bound
                miRatioItem.Click += (s, args) =>
                {
                    _currentAspectRatio = val;
                    try { _ratioCallback?.Invoke(val); } catch { }
                };
                miRatio.Items.Add(miRatioItem);
                _ratioMenuItems.Add(miRatioItem);
            }
            miVideo.Items.Add(miRatio);
            RegisterRatioMenu(cm);

            var miDecoder = new MenuItem { Header = Localizer.S("Menu_Decoder", "解码器") };
            foreach (var val in DecoderOptions.AllDecoders)
            {
                var label = DecoderOptions.GetDisplayName(val);
                var miDecoderItem = new MenuItem { Header = label, Tag = val, IsCheckable = true, IsChecked = string.Equals(val, _currentDecoder, StringComparison.OrdinalIgnoreCase) };
                miDecoderItem.Click += (s, args) =>
                {
                    _currentDecoder = val;
                    if (_decoderCallback != null)
                        _decoderCallback(val);
                };
                miDecoder.Items.Add(miDecoderItem);
                _decoderMenuItems.Add(miDecoderItem);
            }
            miVideo.Items.Add(miDecoder);

            var miDeinterlace = new MenuItem
            {
                Header = Localizer.S("Menu_Deinterlace", "反交错"),
                IsCheckable = true,
                IsChecked = !string.Equals(AppSettings.Current.Deinterlace, "no", StringComparison.OrdinalIgnoreCase)
            };
            Wire(miDeinterlace, toggleDeinterlace);
            miVideo.Items.Add(miDeinterlace);

            // Embedded subtitle tracks are only known once a file is playing, so the list is rebuilt
            // every time the submenu opens (issue #35).
            var miSubtitle = new MenuItem { Header = Localizer.S("Menu_Subtitle", "字幕") };
            miSubtitle.SubmenuOpened += (s, args) => RebuildSubtitleMenu(miSubtitle);
            miVideo.Items.Add(miSubtitle);

            var miTimeshift = new MenuItem { Header = Localizer.S("Menu_Timeshift", "时间偏移"), IsCheckable = true };
            miVideo.Items.Add(miTimeshift);

            var miMinimal = new MenuItem
            {
                Header = Localizer.S("Menu_MinimalMode", "精简模式"),
                InputGestureText = "Ctrl+Shift+L",
                IsCheckable = true,
                IsChecked = isMinimalChecked
            };
            Wire(miMinimal, toggleMinimal);
            miVideo.Items.Add(miMinimal);

            cm.Items.Add(miVideo);

            // 5. 声音
            var miSound = new MenuItem { Header = Localizer.S("Menu_Sound", "声音") };
            var miMute = new MenuItem { Header = Localizer.S("Menu_Mute", "静音"), InputGestureText = "M" };
            Wire(miMute, toggleMute);
            miSound.Items.Add(miMute);

            var miVolUp = new MenuItem { Header = Localizer.S("Menu_VolumeUp", "音量+"), InputGestureText = "=" };
            Wire(miVolUp, volumeUp);
            miSound.Items.Add(miVolUp);

            var miVolDown = new MenuItem { Header = Localizer.S("Menu_VolumeDown", "音量-"), InputGestureText = "-" };
            Wire(miVolDown, volumeDown);
            miSound.Items.Add(miVolDown);

            cm.Items.Add(miSound);

            // 6. 网络
            var miNetwork = new MenuItem { Header = Localizer.S("Menu_Network", "网络") };
            var miUdp = new MenuItem
            {
                Header = Localizer.S("Menu_UDP", "UDP组播优化"),
                IsCheckable = true,
                IsChecked = AppSettings.Current.EnableUdpOptimization
            };
            Wire(miUdp, toggleUdp);
            miNetwork.Items.Add(miUdp);

            var miFcc = new MenuItem
            {
                Header = Localizer.S("Menu_FCC", "FCC快速切台"),
                IsCheckable = true,
                IsChecked = AppSettings.Current.FccPrefetchCount > 0
            };
            Wire(miFcc, toggleFcc);
            miNetwork.Items.Add(miFcc);

            cm.Items.Add(miNetwork);

            // [分隔线] 网络 ↔ 录制
            cm.Items.Add(new Separator());

            // 7. 录制
            var miRecord = new MenuItem { Header = Localizer.S("Menu_Record", "录制") };
            var miRecNow = new MenuItem { Header = Localizer.S("Menu_RecordNow", "实时录制"), InputGestureText = "R", IsCheckable = true };
            miRecNow.Click += (s, args) =>
            {
                if (_recToggleCallback != null)
                    _recToggleCallback(miRecNow.IsChecked);
            };
            miRecord.Items.Add(miRecNow);

            var miScheduledRec = new MenuItem { Header = Localizer.S("Menu_ScheduledRecording", "正在录制") };
            miScheduledRec.Click += (s, args) =>
            {
                try
                {
                    var win = new LibmpvIptvClient.ScheduledRecordingListWindow();
                    win.Owner = System.Windows.Application.Current.MainWindow;
                    win.Show();
                }
                catch { }
            };
            miRecord.Items.Add(miScheduledRec);

            var miRecList = new MenuItem { Header = Localizer.S("Menu_RecordingsList", "录制列表") };
            miRecList.Click += (s, args) =>
            {
                if (_recListCallback != null)
                    _recListCallback();
            };
            miRecord.Items.Add(miRecList);

            var miRecSettings = new MenuItem { Header = Localizer.S("Menu_RecordSettings", "录制设置") };
            miRecSettings.Click += (s, args) =>
            {
                if (_recSettingsCallback != null)
                    _recSettingsCallback(5);
            };
            miRecord.Items.Add(miRecSettings);

            cm.Items.Add(miRecord);

            // 8. 预约
            var miReminder = new MenuItem { Header = Localizer.S("Menu_Reminders", "预约") };
            miReminder.Click += (s, a) =>
            {
                try
                {
                    LibmpvIptvClient.Helpers.ReminderWindowManager.OpenOrActivate();
                }
                catch { }
            };
            cm.Items.Add(miReminder);

            // [分隔线] 预约 ↔ 应用
            cm.Items.Add(new Separator());

            // 9. 应用
            var miApp = new MenuItem { Header = Localizer.S("Menu_Application", "应用") };
            var miSettings = new MenuItem { Header = Localizer.S("Menu_Settings", "设置"), InputGestureText = "Ctrl+," };
            Wire(miSettings, openSettings);
            miApp.Items.Add(miSettings);

            var miAbout = new MenuItem { Header = Localizer.S("Menu_About", "关于"), InputGestureText = "Ctrl+I" };
            Wire(miAbout, showAbout);
            miApp.Items.Add(miAbout);

            var miTopmost = new MenuItem
            {
                Header = Localizer.S("Menu_Topmost", "置顶"),
                IsCheckable = true,
                IsChecked = isTopmostChecked
            };
            Wire(miTopmost, toggleTopmost);
            miApp.Items.Add(miTopmost);

            var miDebug = new MenuItem { Header = Localizer.S("Menu_Debug", "调试"), InputGestureText = "F1" };
            Wire(miDebug, openDebug);
            miApp.Items.Add(miDebug);

            var miShortcuts = new MenuItem { Header = Localizer.S("Menu_Shortcuts", "快捷键说明"), InputGestureText = "Ctrl+/" };
            Wire(miShortcuts, showShortcuts);
            miApp.Items.Add(miShortcuts);

            // 清除关闭记忆 - 仅在已记住时显示（放在快捷键说明下面）
            if (!string.IsNullOrEmpty(AppSettings.Current.CloseMode))
            {
                var miClearCloseMode = new MenuItem { Header = Localizer.S("Menu_ClearCloseMode", "清除关闭记忆") };
                miClearCloseMode.Click += (s, args) =>
                {
                    AppSettings.Current.CloseMode = "";
                    AppSettings.Current.Save();
                    clearCloseMode?.Invoke();
                };
                miApp.Items.Add(miClearCloseMode);
            }

            cm.Items.Add(miApp);

            // 10. 退出
            var miExit = new MenuItem { Header = Localizer.S("Menu_Exit", "退出"), InputGestureText = "Alt+F4" };
            Wire(miExit, exitApp);
            cm.Items.Add(miExit);

            return cm;
        }

        private static Action<double>? _speedCallback;
        private static Action<string>? _ratioCallback;
        private static Action<bool>? _recToggleCallback;
        private static Action? _recListCallback;
        private static Action<int>? _recSettingsCallback;
        private static Action? _switchSourceCallback;
        private static Action<string>? _decoderCallback;
        private static string _currentAspectRatio = "default";
        private static string _currentDecoder = "auto";
        private static double _currentSpeed = 1.0;
        private static readonly List<System.Windows.Controls.MenuItem> _ratioMenuItems = new();
        private static readonly List<System.Windows.Controls.ContextMenu> _ratioMenus = new();
        private static readonly List<System.Windows.Controls.MenuItem> _decoderMenuItems = new();
        private static readonly List<System.Windows.Controls.MenuItem> _speedMenuItems = new();
        private static readonly List<System.Windows.Controls.ContextMenu> _speedMenus = new();

        public static void SetSpeedCallback(Action<double> callback) => _speedCallback = callback;
        public static void SetRatioCallback(Action<string> callback) => _ratioCallback = callback;

        /// <summary>
        /// Wires a menu item to a handler, disabling it when no handler is bound. The title bar menu
        /// passes null for most commands, and a clickable item that silently does nothing is worse than a
        /// greyed out one.
        /// </summary>
        static void Wire(MenuItem item, Action? handler)
        {
            if (handler == null) { item.IsEnabled = false; return; }
            item.Click += (s, e) => { try { handler(); } catch { } };
        }

        /// <summary>Checkable variant: the handler receives the item's new checked state.</summary>
        static void Wire(MenuItem item, Action<bool>? handler)
        {
            if (handler == null) { item.IsEnabled = false; return; }
            item.Click += (s, e) => { try { handler(item.IsChecked); } catch { } };
        }

        static Func<IEnumerable<Models.SubtitleTrackInfo>>? _subtitleTrackProvider;
        static Action<int?>? _subtitleSelectCallback;
        static List<Models.SubtitleTrackInfo> _subtitleTracks = new List<Models.SubtitleTrackInfo>();

        /// <summary>Binds the embedded subtitle track source and the selection handler.</summary>
        public static void SetSubtitleCallbacks(Func<IEnumerable<Models.SubtitleTrackInfo>>? trackProvider, Action<int?> select)
        {
            _subtitleTrackProvider = trackProvider;
            _subtitleSelectCallback = select;
        }

        /// <summary>Re-reads the current subtitle tracks and fills the submenu (called when it opens).</summary>
        static void RebuildSubtitleMenu(MenuItem parent)
        {
            try
            {
                _subtitleTracks = _subtitleTrackProvider?.Invoke()?.Where(t => t != null).ToList()
                                 ?? new List<Models.SubtitleTrackInfo>();
            }
            catch { _subtitleTracks = new List<Models.SubtitleTrackInfo>(); }

            parent.Items.Clear();

            var miOff = new MenuItem
            {
                Header = Localizer.S("Subtitle_Off", "关闭字幕"),
                IsCheckable = true,
                IsChecked = _subtitleTracks.All(t => !t.Selected)
            };
            miOff.Click += (s, args) => { try { _subtitleSelectCallback?.Invoke(null); } catch { } };
            parent.Items.Add(miOff);

            parent.Items.Add(new Separator());

            if (_subtitleTracks.Count == 0)
            {
                parent.Items.Add(new MenuItem
                {
                    Header = Localizer.S("Subtitle_None", "未检测到内嵌字幕"),
                    IsEnabled = false
                });
                return;
            }

            foreach (var track in _subtitleTracks)
            {
                var id = track.Id;
                var miTrack = new MenuItem
                {
                    Header = Services.SubtitleTrackService.Describe(track),
                    IsCheckable = true,
                    IsChecked = track.Selected,
                    Tag = id
                };
                miTrack.Click += (s, args) => { try { _subtitleSelectCallback?.Invoke(id); } catch { } };
                parent.Items.Add(miTrack);
            }
        }
        public static void SetDecoderCallback(Action<string> callback) => _decoderCallback = callback;
        public static void SetCurrentDecoder(string decoder) { _currentDecoder = decoder; }
        public static void RefreshAllDecoderChecks(string currentDecoder)
        {
            _currentDecoder = currentDecoder;
            foreach (var mi in _decoderMenuItems)
            {
                mi.IsChecked = string.Equals(mi.Tag?.ToString(), currentDecoder, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void SetRecToggleCallback(Action<bool> callback) => _recToggleCallback = callback;
        public static void SetRecListCallback(Action callback) => _recListCallback = callback;
        public static void SetRecSettingsCallback(Action<int> callback) => _recSettingsCallback = callback;
        public static void SetSwitchSourceCallback(Action callback) => _switchSourceCallback = callback;
        public static void SetCurrentAspectRatio(string ratio) => _currentAspectRatio = ratio;
        public static void SetCurrentSpeed(double speed) => _currentSpeed = speed;

        public static void RefreshAllRatioChecks(string currentRatio)
        {
            _currentAspectRatio = currentRatio;
            System.Diagnostics.Debug.WriteLine($"[MenuBuilder] RefreshAllRatioChecks: {currentRatio}, _ratioMenuItems: {_ratioMenuItems.Count}, _ratioMenus: {_ratioMenus.Count}");
            foreach (var mi in _ratioMenuItems)
            {
                var val = mi.Tag as string;
                mi.IsChecked = !string.IsNullOrEmpty(val) && string.Equals(val, currentRatio, StringComparison.OrdinalIgnoreCase);
                System.Diagnostics.Debug.WriteLine($"[MenuBuilder] _ratioMenuItems: Header={mi.Header}, Tag={val}, IsChecked={mi.IsChecked}");
            }
            var knownRatios = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default", "16:9", "4:3", "stretch", "fill", "crop" };
            foreach (var menu in _ratioMenus.ToList())
            {
                try
                {
                    UpdateRatioItemsRecursive(menu.Items, currentRatio, knownRatios);
                    foreach (var topItem in menu.Items)
                    {
                        if (topItem is System.Windows.Controls.MenuItem topMi && topMi.HasItems)
                        {
                            foreach (var subItem in topMi.Items)
                            {
                                if (subItem is System.Windows.Controls.MenuItem subMi && subMi.HasItems)
                                {
                                    UpdateRatioItemsRecursive(subMi.Items, currentRatio, knownRatios);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[MenuBuilder] Exception: {ex.Message}"); }
            }
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var mi in _ratioMenuItems)
                    {
                        var val = mi.Tag as string;
                        mi.IsChecked = !string.IsNullOrEmpty(val) && string.Equals(val, currentRatio, StringComparison.OrdinalIgnoreCase);
                    }
                    foreach (var menu in _ratioMenus.ToList())
                    {
                        UpdateRatioItemsRecursive(menu.Items, currentRatio, knownRatios);
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch { }
        }

        private static void UpdateRatioItemsRecursive(ItemCollection items, string currentRatio, HashSet<string> knownRatios)
        {
            foreach (var item in items)
            {
                if (item is System.Windows.Controls.MenuItem mi)
                {
                    if (mi.IsCheckable && mi.Tag is string tag && knownRatios.Contains(tag))
                    {
                        mi.IsChecked = string.Equals(tag, currentRatio, StringComparison.OrdinalIgnoreCase);
                        System.Diagnostics.Debug.WriteLine($"[MenuBuilder] Recursive update: Header={mi.Header}, Tag={tag}, IsChecked={mi.IsChecked}");
                    }
                    if (mi.HasItems)
                    {
                        UpdateRatioItemsRecursive(mi.Items, currentRatio, knownRatios);
                    }
                }
            }
        }

        public static void RegisterRatioMenu(System.Windows.Controls.ContextMenu menu)
        {
            if (!_ratioMenus.Contains(menu))
                _ratioMenus.Add(menu);
        }

        public static void ClearRatioMenus()
        {
            _ratioMenus.Clear();
            _ratioMenuItems.Clear();
        }

        public static void AddRatioMenuItems(IEnumerable<System.Windows.Controls.MenuItem> items)
        {
            _ratioMenuItems.AddRange(items);
        }

        public static void RemoveRatioMenuItems(IEnumerable<System.Windows.Controls.MenuItem> items)
        {
            foreach (var item in items)
            {
                _ratioMenuItems.Remove(item);
            }
        }

        public static void RefreshRatioMenuChecks(ContextMenu menu, string currentRatio)
        {
            try
            {
                foreach (var item in menu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.Header?.ToString() == Localizer.S("Menu_Video", "视频"))
                    {
                        foreach (var subItem in mi.Items)
                        {
                            if (subItem is System.Windows.Controls.MenuItem ratioItem && ratioItem.IsCheckable)
                            {
                                var ratios = new[] { "default", "16:9", "4:3", "stretch", "fill", "crop" };
                                var labelMap = new Dictionary<string, string>
                                {
                                    { "default", Localizer.S("Ratio_Default", "默认") },
                                    { "16:9", "16:9" },
                                    { "4:3", "4:3" },
                                    { "stretch", Localizer.S("Ratio_Stretch", "拉伸") },
                                    { "fill", Localizer.S("Ratio_Fill", "填充") },
                                    { "crop", Localizer.S("Ratio_Crop", "裁剪") }
                                };
                                foreach (var r in ratios)
                                {
                                    if (ratioItem.Header?.ToString() == labelMap[r])
                                    {
                                        ratioItem.IsChecked = string.Equals(r, currentRatio, StringComparison.OrdinalIgnoreCase);
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        public static void RegisterSpeedMenu(System.Windows.Controls.ContextMenu menu)
        {
            if (!_speedMenus.Contains(menu))
                _speedMenus.Add(menu);
        }

        public static void AddSpeedMenuItems(IEnumerable<System.Windows.Controls.MenuItem> items)
        {
            _speedMenuItems.AddRange(items);
        }

        public static void RemoveSpeedMenuItems(IEnumerable<System.Windows.Controls.MenuItem> items)
        {
            foreach (var item in items)
            {
                _speedMenuItems.Remove(item);
            }
        }

        public static void RefreshAllSpeedChecks(double currentSpeed)
        {
            _currentSpeed = currentSpeed;
            foreach (var mi in _speedMenuItems)
            {
                if (mi.Tag is double sp)
                    mi.IsChecked = Math.Abs(sp - currentSpeed) < 0.001;
            }
            foreach (var menu in _speedMenus.ToList())
            {
                try
                {
                    UpdateSpeedItemsRecursive(menu.Items, currentSpeed);
                }
                catch { }
            }
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var mi in _speedMenuItems)
                    {
                        if (mi.Tag is double sp)
                            mi.IsChecked = Math.Abs(sp - currentSpeed) < 0.001;
                    }
                    foreach (var menu in _speedMenus.ToList())
                    {
                        UpdateSpeedItemsRecursive(menu.Items, currentSpeed);
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch { }
        }

        private static void UpdateSpeedItemsRecursive(System.Windows.Controls.ItemCollection items, double currentSpeed)
        {
            foreach (var item in items)
            {
                if (item is System.Windows.Controls.MenuItem mi)
                {
                    if (mi.IsCheckable && mi.Tag is double sp)
                    {
                        mi.IsChecked = Math.Abs(sp - currentSpeed) < 0.001;
                    }
                    if (mi.HasItems)
                    {
                        UpdateSpeedItemsRecursive(mi.Items, currentSpeed);
                    }
                }
            }
        }
    }
}