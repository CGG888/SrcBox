using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Helpers;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient
{
    public partial class MainWindow : Window
    {
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var closeMode = AppSettings.Current.CloseMode;
                if (!string.IsNullOrEmpty(closeMode))
                {
                    if (closeMode == "exit")
                    {
                        try { _mpv?.Dispose(); } catch { }
                        System.Windows.Application.Current.Shutdown();
                        return;
                    }
                    else if (closeMode == "minimize_to_tray")
                    {
                        e.Cancel = true;
                        try { if (_shell.WindowStateActions.IsFullscreen) ToggleFullscreen(false); } catch { }
                        Hide();
                        return;
                    }
                }

                if (AppSettings.Current.ConfirmOnClose)
                {
                    e.Cancel = true;
                    var title = LibmpvIptvClient.Helpers.Localizer.S("CloseConfirm_Title", "关闭确认");
                    var label = LibmpvIptvClient.Helpers.Localizer.S("CloseConfirm_Label", "选择操作：");
                    var yesLine = LibmpvIptvClient.Helpers.Localizer.S("CloseConfirm_LineYes", "是：退出软件");
                    var noLine = LibmpvIptvClient.Helpers.Localizer.S("CloseConfirm_LineNo", "否：最小化到系统托盘");
                    var msg = label + Environment.NewLine + Environment.NewLine + yesLine + Environment.NewLine + noLine;
                    var owner = (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null) ? (Window)_shell.WindowStateActions.FullscreenWindow : this;
                    var (r, remember) = ModernMessageBox.ShowWithRemember(owner, msg, title, MessageBoxButton.YesNo);
                    if (r.HasValue && r.Value == true)
                    {
                        if (remember)
                        {
                            AppSettings.Current.CloseMode = "exit";
                            AppSettings.Current.Save();
                        }
                        try { _mpv?.Dispose(); } catch { }
                        System.Windows.Application.Current.Shutdown();
                        return;
                    }
                    else if (r.HasValue && r.Value == false)
                    {
                        if (remember)
                        {
                            AppSettings.Current.CloseMode = "minimize_to_tray";
                            AppSettings.Current.Save();
                        }
                        try { if (_shell.WindowStateActions.IsFullscreen) ToggleFullscreen(false); } catch { }
                        Hide();
                        return;
                    }
                    return;
                }
            }
            catch { }
            base.OnClosing(e);
        }
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        internal void EnableDarkTitleBarFromManager() => TryEnableDarkTitleBar();
        void TryEnableDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int value = 1;
                DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
            }
            catch { }
        }

        void Timer_Tick(object? sender, EventArgs e)
        {
            _playbackTickManager?.Timer_Tick(sender, e);
        }

        void ListHistory_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _historyManager?.ListHistory_MouseDoubleClick(sender, e);
        }
        void HistoryDeleteOne_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            _historyManager?.HistoryDeleteOne_Executed(sender, e);
        }
        void HistoryDeleteOne_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            _historyManager?.HistoryDeleteOne_CanExecute(sender, e);
        }
        void BtnHistoryDelete_Click(object sender, RoutedEventArgs e)
        {
            _historyManager?.BtnHistoryDelete_Click(sender, e);
        }
        void BtnHistoryClear_Click(object sender, RoutedEventArgs e)
        {
            _historyManager?.BtnHistoryClear_Click(sender, e);
        }

        void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            _menuManager?.BtnSpeed_Click(sender, e);
        }

        void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            _shell.IsMuted = !_shell.IsMuted;
        }
        void UpdatePlayPauseIcon()
        {
            try
            {
                if (TaskbarItemInfo != null)
                {
                    TaskbarItemInfo.ProgressState = _shell.IsPaused
                        ? System.Windows.Shell.TaskbarItemProgressState.Paused
                        : System.Windows.Shell.TaskbarItemProgressState.Normal;
                }
            }
            catch { }
        }
        internal void UpdatePlayPauseIconFromManager() => UpdatePlayPauseIcon();

        internal void OpenSettings(int tabIndex = 0)
        {
            _settingsManager?.OpenSettings(tabIndex);
        }

        internal void ShowShortcuts()
        {
            var wnd = new ShortcutsWindow { Owner = this };
            wnd.ShowDialog();
        }

        internal Rect GetVideoAreaRect()
        {
            try
            {
                return new Rect(VideoHost.PointToScreen(new System.Windows.Point(0, 0)), new System.Windows.Size(VideoHost.ActualWidth, VideoHost.ActualHeight));
            }
            catch
            {
                return new Rect(Left, Top, ActualWidth, ActualHeight);
            }
        }

        internal void ShowShortcutsDialogIfNeeded()
        {
            if (!AppSettings.Current.SkipShortcutsDialog)
            {
                ShowShortcuts();
            }
        }

        internal void ApplyDecoder(string decoder)
        {
            AppSettings.Current.Decoder = decoder;
            AppSettings.Current.Save();
            try { Diagnostics.Logger.Info($"[Decoder] 设置已更改: {decoder}"); } catch { }
            try
            {
                if (PlayerInterop != null)
                {
                    PlayerInterop.SetSettings(AppSettings.Current);
                    PlayerInterop.SetHwdec(decoder);
                }
            }
            catch { }
            MenuBuilder.RefreshAllDecoderChecks(decoder);
        }

        internal void SetDrawerCollapsed(bool collapsed)
        {
            if (_shell.IsDrawerCollapsed == collapsed) return;
            var drawerWidth = _shell.DrawerWidth > 0 ? _shell.DrawerWidth : 380;
            if (!collapsed)
            {
                Width = _baseWindowWidth + (CbEpg.IsChecked == true ? 320 : 0) + drawerWidth;
            }
            else
            {
                Width = _baseWindowWidth + (CbEpg.IsChecked == true ? 320 : 0);
            }
            _shell.IsDrawerCollapsed = collapsed;
            DrawerPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            if (!collapsed)
            {
                try { _shell.ApplyChannelFilter(); } catch { }
                try { ListChannels.Items.Refresh(); } catch { }
                try { ListGroups.Items.Refresh(); } catch { }
            }
        }
        void BtnDrawerCollapse_Click(object sender, RoutedEventArgs e)
        {
            try { SetDrawerCollapsed(true); } catch { }
        }

        void OnClosed(object? sender, EventArgs e)
        {
            try
            {
                _overlayManager.Close();
                SourceHealthService.Instance.Stop();
                try { _recordingManager?.Close(); } catch { }
                if (_shell.WindowStateActions.FullscreenWindow != null)
                {
                    _shell.WindowStateActions.FullscreenWindow.Close();
                    _shell.WindowStateActions.FullscreenWindow = null;
                }
                if (_mpv != null)
                {
                    _mpv.Dispose();
                    _mpv = null;
                }
                App.LanguageChanged -= OnLanguageChanged;
                App.ThemeChanged -= OnThemeChanged;
                try { ReminderListWindow.RemindersChanged -= _epgRemindersChangedHandler; } catch { }
                try { _minimalToolbarHideTimer?.Stop(); } catch { }
                try { _minimalPointerWatchTimer?.Stop(); } catch { }
                try
                {
                    if (_minimalToolbarPanel != null && VideoPanel != null)
                    {
                        VideoPanel.Controls.Remove(_minimalToolbarPanel);
                    }
                }
                catch { }
                try { _minimalBtnFullscreen?.Dispose(); } catch { }
                try { _minimalBtnWindow?.Dispose(); } catch { }
                try { _minimalBtnClose?.Dispose(); } catch { }
                try { _minimalToolbarPanel?.Dispose(); } catch { }
                _minimalBtnFullscreen = null;
                _minimalBtnWindow = null;
                _minimalBtnClose = null;
                _minimalToolbarPanel = null;
            }
            catch { }
        }
        void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_mpv == null) return;
            var url = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8";
            _mpv.LoadFile(url);
        }

        void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
        }
        void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            try { _shell.SearchText = ""; } catch { }
        }

        void CbM3uList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingM3uList) return;
            if (sender is System.Windows.Controls.ComboBox cb && cb.SelectedItem is M3uSource src)
            {
                if (cb.Name == nameof(CbM3uList))
                    _shell.MenuActions.LoadM3u(src);
            }
        }

        void TxtSearchGroups_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
        }

        void BtnClearSearchGroups_Click(object sender, RoutedEventArgs e)
        {
            try { _shell.SearchText = ""; } catch { }
        }

        void BtnChannelsRefreshGroups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = AppSettings.Current.SavedSources?.FirstOrDefault(s => s.IsSelected);
                if (selected != null)
                {
                    _shell.MenuActions.LoadM3u(selected);
                }
                else
                {
                    _shell.ApplyChannelFilter();
                }
            }
            catch { }
        }

        void ListGroups_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox list && list.SelectedItem is LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.ChannelGroupItem item)
            {
                _shell.SelectedGroup = item.Name;
            }
            else
            {
                _shell.SelectedGroup = null;
            }
        }

        private string? _activeGroupForMove;

        private void GroupSelectIndicator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.Tag is string groupName)
                {
                    _activeGroupForMove = groupName;
                    UpdateGroupSelectionIndicators();
                }
            }
            catch { }
        }

        private void GroupHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.TextBlock tb && tb.DataContext is LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.ChannelGroupItem item)
                {
                    _activeGroupForMove = item.Name;
                    UpdateGroupSelectionIndicators();
                }
            }
            catch { }
        }

        private void GroupExpander_Expanded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Expander expander && expander.DataContext is LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.ChannelGroupItem item)
                {
                    _activeGroupForMove = item.Name;
                    UpdateGroupSelectionIndicators();
                }
            }
            catch { }
        }

        private void UpdateGroupSelectionIndicators()
        {
            try
            {
                if (ListGroups?.Items == null) return;
                foreach (var item in ListGroups.Items)
                {
                    if (item is LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.ChannelGroupItem group)
                    {
                        var container = ListGroups.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.ContentPresenter;
                        if (container != null)
                        {
                            var expander = FindVisualChild<System.Windows.Controls.Expander>(container);
                            if (expander?.Header is System.Windows.Controls.DockPanel dock)
                            {
                                foreach (var child in dock.Children)
                                {
                                    if (child is System.Windows.Controls.Button btn && btn.Tag != null)
                                    {
                                        bool isSelected = btn.Tag.ToString() == _activeGroupForMove;
                                        btn.Foreground = isSelected
                                            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                            : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                if (FindVisualChild<T>(child) is T found) return found;
            }
            return null;
        }

        private void BtnMoveGroupUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var groupName = _activeGroupForMove ?? _shell.ChannelGroups?.FirstOrDefault()?.Name;
                if (!string.IsNullOrEmpty(groupName))
                {
                    MoveGroup(groupName, -1);
                }
            }
            catch { }
        }

        private void BtnMoveGroupDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var groupName = _activeGroupForMove ?? _shell.ChannelGroups?.FirstOrDefault()?.Name;
                if (!string.IsNullOrEmpty(groupName))
                {
                    MoveGroup(groupName, 1);
                }
            }
            catch { }
        }

        private void MoveGroup(string groupName, int direction)
        {
            try
            {
                var order = AppSettings.Current.ChannelGroupOrder;
                if (order == null) order = new List<string>();

                int idx = order.IndexOf(groupName);
                if (idx < 0)
                {
                    order = _shell.ChannelGroups?.Select(g => g.Name).ToList() ?? new List<string>();
                    idx = order.IndexOf(groupName);
                }
                if (idx < 0) return;

                int newIdx = idx + direction;
                if (newIdx < 0 || newIdx >= order.Count) return;

                order.RemoveAt(idx);
                order.Insert(newIdx, groupName);
                AppSettings.Current.ChannelGroupOrder = order;
                AppSettings.Current.Save();

                _shell.UpdateGroups();
            }
            catch { }
        }

        void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var ch = _shell.ChannelInteractionActions.ResolveDoubleClickChannel(e, sender);
                if (ch != null)
                {
                    PlayChannel(ch);
                    e.Handled = true;
                }
            }
            catch { }
        }
        void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is Channel ch)
                {
                    try
                    {
                        _shell.ChannelInteractionActions.ToggleFavorite(
                            ch,
                            c => UserDataStore.ComputeKey(c),
                            (key, favorite) => _userDataStore.SetFavorite(key, favorite));
                    }
                    catch { }
                    _shell.UpdateFavorites();
                }
            }
            catch { }
        }
        void PlayChannel(Channel ch)
        {
            if (_mpv == null || ch == null) return;
            Behaviors.ChannelPreviewBehavior.CloseActivePopupIfAny();
            _shell.ChannelPlaybackActions.PlayChannel(ch, ListEpg.ItemsSource as IEnumerable<EpgProgram>);
        }
        void UpdateEpgDisplay()
        {
            _epgManager?.UpdateEpgDisplay();
        }
        void SyncEpgReminderList()
        {
            _epgManager?.SyncEpgReminderList();
        }
        internal void CbEpg_Click(object sender, RoutedEventArgs e)
        {
            var show = _shell.ViewToggleActions.ResolveEpgVisible(CbEpg.IsChecked);
            if (_shell.WindowStateActions.IsFullscreen)
            {
                if (show) ShowFullscreenEpg();
                else CloseFullscreenEpg();
            }
            else
            {
                var drawerWidth = _shell.DrawerWidth > 0 ? _shell.DrawerWidth : 380;
                if (show)
                {
                    Width = _baseWindowWidth + 320 + (_shell.IsDrawerCollapsed ? 0 : drawerWidth);
                }
                else
                {
                    Width = _baseWindowWidth + (_shell.IsDrawerCollapsed ? 0 : drawerWidth);
                }
                _epgManager?.CbEpg_Click(sender, e);
            }
        }
        void BtnEpgCollapse_Click(object sender, RoutedEventArgs e)
        {
            _epgManager?.BtnEpgCollapse_Click(sender, e);
        }
        private class EpgDateItem
        {
            public DateTime Date { get; set; }
            public string Label { get; set; } = "";
        }

        void RefreshEpgList(Channel ch)
        {
            _epgManager?.RefreshEpgList(ch);
        }
        void RefreshEpgList(Channel ch, DateTime focusTime)
        {
            _epgManager?.RefreshEpgList(ch, focusTime);
        }

        void UpdateEpgDateUI()
        {
            _epgManager?.UpdateEpgDateUI();
        }

        void BtnPrevDay_Click(object sender, RoutedEventArgs e)
        {
            _epgManager?.BtnPrevDay_Click(sender, e);
        }

        void BtnNextDay_Click(object sender, RoutedEventArgs e)
        {
            _epgManager?.BtnNextDay_Click(sender, e);
        }

        void FilterEpgList()
        {
            _epgManager?.FilterEpgList();
        }
        void ListEpg_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _epgManager?.ListEpg_PreviewMouseLeftButtonDown(sender, e);
        }
        void EpgMenu_Remind_Click(object sender, RoutedEventArgs e)
        {
            _epgManager?.EpgMenu_Remind_Click(sender, e);
        }
        void EpgRemindButton_Click(object sender, RoutedEventArgs e)
        {
            _epgManager?.EpgRemindButton_Click(sender, e);
        }
        public Rect GetVideoScreenRect()
        {
            try
            {
                FrameworkElement anchor = VideoHost ?? (FrameworkElement)this;
                double w = anchor.ActualWidth;
                double h = anchor.ActualHeight;
                if (w < 50 || h < 50)
                {
                    var winTopLeft = this.PointToScreen(new System.Windows.Point(0, 0));
                    var dpiw = VisualTreeHelper.GetDpi(this);
                    double wl = winTopLeft.X / dpiw.DpiScaleX;
                    double wt = winTopLeft.Y / dpiw.DpiScaleY;
                    double ww = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                    double wh = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
                    return new Rect(wl, wt, ww, wh);
                }
                var topLeft = anchor.PointToScreen(new System.Windows.Point(0, 0));
                var dpi = VisualTreeHelper.GetDpi(this);
                double left = topLeft.X / dpi.DpiScaleX;
                double top = topLeft.Y / dpi.DpiScaleY;
                if (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null)
                {
                    return new Rect(_shell.WindowStateActions.FullscreenWindow.Left, _shell.WindowStateActions.FullscreenWindow.Top, _shell.WindowStateActions.FullscreenWindow.Width, _shell.WindowStateActions.FullscreenWindow.Height);
                }
                return new Rect(left, top, w, h);
            }
            catch
            {
                var winTopLeft = this.PointToScreen(new System.Windows.Point(0, 0));
                var dpi = VisualTreeHelper.GetDpi(this);
                double wl = winTopLeft.X / dpi.DpiScaleX;
                double wt = winTopLeft.Y / dpi.DpiScaleY;
                double ww = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double wh = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
                return new Rect(wl, wt, ww, wh);
            }
        }

        public void JumpToChannelByIdOrName(string id, string name)
        {
            _shell.ChannelPlaybackActions.JumpToChannelByIdOrName(id, name);
        }

        void AllChannel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Channel ch)
                {
                    _shell.ChannelPlaybackActions.PlayChannel(ch, ListEpg.ItemsSource as IEnumerable<EpgProgram>);
                }
            }
            catch { }
        }
        List<Source> BuildSourcesForChannel(Channel ch)
        {
            return _shell.SourceLoader.BuildSourcesForChannel(ch, _shell.Channels);
        }
        void BtnSources_Click(object sender, RoutedEventArgs e)
        {
            _menuManager?.BtnSources_Click(sender, e);
        }
        void BtnRatio_Click(object sender, RoutedEventArgs e)
        {
            _menuManager?.BtnRatio_Click(sender, e);
        }
        void OpenSourceMenuAtButton(System.Windows.Controls.Primitives.ToggleButton target)
        {
            _menuManager?.OpenSourceMenuAtButton(target);
        }
        void OpenSourceMenuAtOverlay()
        {
            _menuManager?.OpenSourceMenuAtOverlay();
        }

        public void HandleScheduledRecordingTrigger(Services.ScheduledRecordingTriggerArgs e)
        {
            try
            {
                var exactMatch = _shell.Channels.FirstOrDefault(c =>
                    string.Equals(c.Id, e.ChannelId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Name, e.ChannelName, StringComparison.OrdinalIgnoreCase));

                var channel = exactMatch ?? _shell.Channels.FirstOrDefault(c =>
                    string.Equals(c.TvgId, e.ChannelId, StringComparison.OrdinalIgnoreCase));

                if (channel == null)
                {
                    try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[ScheduledRecord] channel not found: {e.ChannelName}"); } catch { }
                    return;
                }

                var channelName = (exactMatch != null || !string.IsNullOrEmpty(e.ChannelName))
                    ? channel.Name
                    : e.ChannelName;

                // Use custom duration if set, otherwise use EPG scheduled end time
                var scheduledEnd = e.RecordDurationMin.HasValue
                    ? e.ScheduledStart.AddMinutes(e.RecordDurationMin.Value)
                    : e.ScheduledEnd;
                var info = new Models.ScheduledRecordingInfo
                {
                    ReminderId = e.ReminderId,
                    ChannelId = channel.Id,
                    ChannelName = channelName,
                    ChannelLogo = channel.Logo ?? "",
                    ProgramTitle = e.ProgramTitle,
                    ScheduledStart = e.ScheduledStart,
                    ScheduledEnd = scheduledEnd,
                    ScheduledDurationMin = e.RecordDurationMin ?? (int)(e.ScheduledEnd - e.ScheduledStart).TotalMinutes
                };

                if (e.Action == "record_front")
                {
                    if (!ScheduledRecordingManager.Instance.CanStartFrontRecording())
                    {
                        try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[ScheduledRecord] front recording conflict"); } catch { }
                        return;
                    }
                    ScheduledRecordingManager.Instance.Add(info);
                    JumpToChannelByIdOrName(channel.Id, channel.Name);
                    _recordingManager?.StartScheduledFrontRecording(info, channel);
                }
                else if (e.Action == "record_back")
                {
                    if (!ScheduledRecordingManager.Instance.CanStartBackRecording())
                    {
                        try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[ScheduledRecord] back recording limit reached"); } catch { }
                        return;
                    }
                    var source = SelectBestSource(channel.Sources);
                    if (source == null)
                    {
                        try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[ScheduledRecord] no source available for {channel.Name}"); } catch { }
                        return;
                    }
                    info.FilePath = ResolveScheduledRecordingPath(channel, info);
                    ScheduledRecordingManager.Instance.Add(info);
                    var sourceUrl = source.Url;
                    ScheduledRecordingManager.Instance.StartBackRecording(info, (id, url, duration) =>
                    {
                        return new Services.BackgroundRecordingInstance(id, sourceUrl, info.FilePath!, duration);
                    });
                }
            }
            catch (Exception ex)
            {
                try { LibmpvIptvClient.Diagnostics.Logger.Error($"[ScheduledRecord] trigger error: {ex.Message}"); } catch { }
            }
        }

        private string ResolveScheduledRecordingPath(Models.Channel channel, Models.ScheduledRecordingInfo info)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var safeChannel = (channel.Name ?? "unknown").Replace(":", "_").Replace("/", "_").Replace("\\", "_");
                var dir = Path.Combine(baseDir, "recordings", safeChannel);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var startStr = info.ScheduledStart.ToString("yyyyMMdd_HHmmss");
                var endStr = info.ScheduledEnd.ToString("yyyyMMdd_HHmmss");
                var safeTitle = (info.ProgramTitle ?? "rec").Replace(":", "_").Replace("/", "_").Replace("\\", "_").Trim();
                var name = $"{startStr}_{endStr}_{safeTitle}.ts";
                return Path.Combine(dir, name).Replace("\\", "/");
            }
            catch { return ""; }
        }

        private Source? SelectBestSource(System.Collections.Generic.List<Source> sources)
        {
            if (sources == null || sources.Count == 0) return null;
            return sources.OrderByDescending(s => s.Priority)
                          .ThenByDescending(s => (s.Quality?.Height ?? 0) * 100 + (s.Quality?.Bitrate ?? 0))
                          .FirstOrDefault();
        }
    }
}
