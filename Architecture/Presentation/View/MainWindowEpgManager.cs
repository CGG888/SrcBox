using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Helpers;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowEpgManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        private readonly MainWindowOverlayManager _overlayManager;
        private readonly EpgService? _epgService;
        private bool _suppressAutoScroll;
        private string? _lastEpgChannelKey;
        private ScrollViewer? _epgScrollViewer;
        private bool _epgScrollEventsHooked;
        private bool _epgLoadedHooked;
        private bool _ignoreAutoScrollEvents;
        public bool IsAutoScrollSuppressed => _suppressAutoScroll;
        private string? _lastCenteredChannelKey;
        private DateTime _lastCenteredStart;
        private DateTime _lastCenteredEnd;

        public MainWindowEpgManager(MainWindow window, MainShellViewModel shell, MainWindowOverlayManager overlayManager, EpgService? epgService)
        {
            _window = window;
            _shell = shell;
            _overlayManager = overlayManager;
            _epgService = epgService;
        }

        public void UpdateEpgDisplay()
        {
            if (_epgService == null) return;
            _shell.EpgActions.SyncChannelCurrentProgramTitles(
                _shell.Channels,
                ch => _epgService.GetCurrentProgram(ch.TvgId, ch.Name));

            if (_window.EpgPanel.Visibility == Visibility.Visible && _shell.CurrentChannel != null)
            {
                if (AppSettings.Current.Epg.StrictMatchByPlaybackTime && (_shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null))
                {
                    var t = _shell.GetPlaybackLocalTime();
                    if (t.HasValue) { RefreshEpgListInternal(_shell.CurrentChannel, t.Value, false, true); return; }
                }
                RefreshEpgListInternal(_shell.CurrentChannel, null, false, true);
            }
        }

        public void SyncEpgReminderList()
        {
            try
            {
                if (_shell.CurrentChannel == null) return;
                if (_window.EpgPanel.Visibility != Visibility.Visible) return;
                if (_window.ListEpg.ItemsSource is IEnumerable<EpgProgram> items)
                {
                    var key = _shell.CurrentChannel.TvgId ?? _shell.CurrentChannel.Id ?? "";
                    var changed = _shell.EpgReminderSyncActions.SyncBookedFlags(items, key, AppSettings.Current.ScheduledReminders);
                    if (changed)
                    {
                        try { _window.ListEpg.Items.Refresh(); } catch { }
                    }
                }
            }
            catch { }
        }

        public void CbEpg_Click(object sender, RoutedEventArgs e)
        {
            var show = _shell.ViewToggleActions.ResolveEpgVisible(_window.CbEpg.IsChecked);
            if (_shell.WindowStateActions.IsFullscreen)
            {
                if (show) _window.ShowFullscreenEpg();
                else _window.CloseFullscreenEpg();
            }
            else
            {
                _window.EpgColumn.Width = new GridLength(show ? 320 : 0);
                _window.EpgPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            try { _overlayManager.OverlayWpf?.SetEpgVisible(show); } catch { }
            if (show) AttachEpgListInteractions();
            var plan = _shell.ViewToggleActions.BuildEpgRefreshPlan(
                show,
                _shell.CurrentChannel != null,
                AppSettings.Current.Epg.StrictMatchByPlaybackTime,
                _shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null,
                _shell.GetPlaybackLocalTime());
            if (plan.ShouldRefresh && _shell.CurrentChannel != null)
            {
                if (plan.PreferredTime.HasValue) RefreshEpgListInternal(_shell.CurrentChannel, plan.PreferredTime.Value, !_suppressAutoScroll, true);
                else RefreshEpgListInternal(_shell.CurrentChannel, null, !_suppressAutoScroll, true);
            }
        }

        public void BtnEpgCollapse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _window.CbEpg.IsChecked = false;
                CbEpg_Click(_window.CbEpg, new RoutedEventArgs());
            }
            catch { }
        }

        public void RefreshEpgList(Channel ch)
        {
            RefreshEpgListInternal(ch, null, false, true);
        }

        public void RefreshEpgList(Channel ch, DateTime focusTime)
        {
            RefreshEpgListInternal(ch, focusTime, false, true);
        }

        public void RefreshEpgListWithFocus(Channel ch, DateTime focusTime, bool forceScroll)
        {
            RefreshEpgListInternal(ch, focusTime, forceScroll, true);
        }

        public void RefreshEpgListWithFocus(Channel ch, DateTime focusTime, bool forceScroll, bool allowAutoScroll)
        {
            RefreshEpgListInternal(ch, focusTime, forceScroll, allowAutoScroll);
        }

        public void UpdateEpgDateUI()
        {
            var state = _shell.EpgActions.BuildDateNavigationState(_shell.AvailableDates, _shell.CurrentEpgDate, DateTime.Today);
            _shell.CurrentEpgDate = state.CurrentDate;
            _window.TxtCurrentDate.Text = state.Label;
            _window.BtnPrevDay.IsEnabled = state.CanPrev;
            _window.BtnNextDay.IsEnabled = state.CanNext;
        }

        public void BtnPrevDay_Click(object sender, RoutedEventArgs e)
        {
            MarkUserEpgNavigation();
            var next = _shell.EpgActions.MoveToPrevDate(_shell.AvailableDates, _shell.CurrentEpgDate);
            if (next.HasValue)
            {
                _shell.CurrentEpgDate = next.Value;
                UpdateEpgDateUI();
                FilterEpgList();
            }
        }

        public void BtnNextDay_Click(object sender, RoutedEventArgs e)
        {
            MarkUserEpgNavigation();
            var next = _shell.EpgActions.MoveToNextDate(_shell.AvailableDates, _shell.CurrentEpgDate);
            if (next.HasValue)
            {
                _shell.CurrentEpgDate = next.Value;
                UpdateEpgDateUI();
                FilterEpgList();
            }
        }

        public void FilterEpgList()
        {
            if (_shell.CurrentChannel == null) return;
            var programs = _epgService?.GetPrograms(_shell.CurrentChannel.TvgId, _shell.CurrentChannel.TvgName, _shell.CurrentChannel.Name);
            if (programs == null || programs.Count == 0)
            {
                programs = GeneratePlaceholderEpg();
            }
            var filtered = _shell.EpgActions.FilterProgramsForDate(programs, _shell.CurrentEpgDate).Items.ToList();
            try
            {
                var key = _shell.CurrentChannel.TvgId ?? _shell.CurrentChannel.Id ?? "";
                var playbackTime = _shell.PlaybackMode switch
                {
                    PlaybackMode.Timeshift => _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    PlaybackMode.Replay => _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    PlaybackMode.RecordingPlayback => _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    _ => null
                };
                _shell.EpgActions.ApplyProgramFlags(
                    filtered,
                    key,
                    AppSettings.Current.ScheduledReminders,
                    _shell.CurrentPlayingProgram,
                    DateTime.Now,
                    playbackTime);
            }
            catch { }
            _window.ListEpg.ItemsSource = filtered;
            if (!_suppressAutoScroll)
            {
                if (_shell.CurrentEpgDate == DateTime.Today)
                {
                    var now = DateTime.Now;
                    var current = _shell.EpgActions.ResolveScrollTargetForToday(filtered, now);
                    if (current != null) _window.ListEpg.ScrollIntoView(current);
                }
                else
                {
                    if (_window.ListEpg.Items.Count > 0) _window.ListEpg.ScrollIntoView(_window.ListEpg.Items[0]);
                }
            }
        }

        public void AttachEpgListInteractions()
        {
            if (_window.ListEpg == null) return;
            TryHookEpgScrollViewer();
            if (!_epgLoadedHooked)
            {
                _window.ListEpg.Loaded += OnEpgListLoaded;
                _epgLoadedHooked = true;
            }
        }

        private void OnEpgListLoaded(object sender, RoutedEventArgs e)
        {
            TryHookEpgScrollViewer();
        }

        private void TryHookEpgScrollViewer()
        {
            if (_epgScrollEventsHooked) return;
            _epgScrollViewer = FindScrollViewer(_window.ListEpg);
            if (_epgScrollViewer == null) return;
            _epgScrollViewer.PreviewMouseWheel += OnEpgUserScroll;
            _epgScrollViewer.PreviewMouseDown += OnEpgUserScroll;
            _epgScrollViewer.ScrollChanged += OnEpgScrollChanged;
            _epgScrollEventsHooked = true;
        }

        private void OnEpgUserScroll(object? sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_ignoreAutoScrollEvents) return;
            MarkUserEpgNavigation();
        }

        private void OnEpgScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_ignoreAutoScrollEvents) return;
            if (Math.Abs(e.VerticalChange) > 0.1)
            {
                MarkUserEpgNavigation();
            }
        }

        private void MarkUserEpgNavigation()
        {
            _suppressAutoScroll = true;
        }

        public void ResetAutoScrollSuppression()
        {
            _suppressAutoScroll = false;
        }

        private void RefreshEpgListInternal(Channel ch, DateTime? focusTime, bool forceScroll, bool allowAutoScroll)
        {
            if (ch == null) return;
            var key = ch.TvgId ?? ch.Id ?? ch.Name ?? "";
            if (!string.Equals(_lastEpgChannelKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _suppressAutoScroll = false;
                _lastEpgChannelKey = key;
            }

            _window.LblEpgChannel.Text = ch.Name;
            var programs = _epgService?.GetPrograms(ch.TvgId, ch.TvgName, ch.Name);
            try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[EPG] 渲染频道 {ch.Name} EPG 列表，数据条数={(programs?.Count ?? 0)}"); } catch { }

            var suppressAutoScroll = _suppressAutoScroll || !allowAutoScroll;
            var useFocusDate = focusTime.HasValue && (!suppressAutoScroll || forceScroll);
            var focusDate = useFocusDate ? _shell.EpgActions.ResolveDateForFocus(focusTime.Value) : _shell.CurrentEpgDate;
            if (focusDate == default) focusDate = DateTime.Today;
            _shell.CurrentEpgDate = focusDate;
            _shell.AvailableDates.Clear();

            if (programs == null || programs.Count == 0)
            {
                programs = GeneratePlaceholderEpg();
            }
            var dataSet = _shell.EpgActions.BuildEpgDataSet(programs, focusDate);
            _shell.AvailableDates = dataSet.AvailableDates.ToList();
            _shell.CurrentEpgDate = dataSet.CurrentDate;
            UpdateEpgDateUI();
            var filtered = dataSet.Items.ToList();
            try
            {
                var playbackTime = _shell.PlaybackMode switch
                {
                    PlaybackMode.Timeshift => focusTime ?? _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    PlaybackMode.Replay => focusTime ?? _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    PlaybackMode.RecordingPlayback => focusTime ?? _shell.PlaybackFocusTime ?? _shell.GetPlaybackLocalTime() ?? _shell.CurrentPlayingProgram?.Start,
                    _ => null
                };
                _shell.EpgActions.ApplyProgramFlags(
                    filtered,
                    key,
                    AppSettings.Current.ScheduledReminders,
                    _shell.CurrentPlayingProgram,
                    DateTime.Now,
                    playbackTime);
            }
            catch { }
            var priorOffset = suppressAutoScroll ? _epgScrollViewer?.VerticalOffset : null;
            _window.ListEpg.ItemsSource = filtered;
            try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[EPG] 日期 {_shell.CurrentEpgDate:yyyy-MM-dd} 可见节目数 {filtered.Count}"); } catch { }

            if (forceScroll || !suppressAutoScroll)
            {
                if (focusTime.HasValue)
                {
                    var target = _shell.EpgSelectionSyncActions.ResolveSelectionTarget(filtered, focusTime.Value);
                    if (target == null) target = filtered.FirstOrDefault(p => p.IsPlaying);
                    if (target != null && (forceScroll || ShouldCenterTarget(target, key))) ScrollItemIntoViewCentered(target);
                    else if (_window.ListEpg.Items.Count > 0) _window.ListEpg.ScrollIntoView(_window.ListEpg.Items[0]);
                }
                else if (_shell.CurrentEpgDate == DateTime.Today)
                {
                    var now = DateTime.Now;
                    var current = _shell.EpgActions.ResolveScrollTargetForToday(filtered, now);
                    if (current == null) current = filtered.FirstOrDefault(p => p.IsPlaying);
                    if (current != null && (forceScroll || ShouldCenterTarget(current, key))) ScrollItemIntoViewCentered(current);
                }
                else
                {
                    if (_window.ListEpg.Items.Count > 0) _window.ListEpg.ScrollIntoView(_window.ListEpg.Items[0]);
                }
            }
            else if (priorOffset.HasValue && _epgScrollViewer != null)
            {
                _window.ListEpg.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _ignoreAutoScrollEvents = true;
                        var max = Math.Max(0, _epgScrollViewer.ExtentHeight - _epgScrollViewer.ViewportHeight);
                        var target = Math.Max(0, Math.Min(max, priorOffset.Value));
                        _epgScrollViewer.ScrollToVerticalOffset(target);
                    }
                    catch { }
                    finally { _ignoreAutoScrollEvents = false; }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ScrollItemIntoViewCentered(EpgProgram target)
        {
            if (target == null) return;
            if (_window.ListEpg == null) return;
            _ignoreAutoScrollEvents = true;
            _window.ListEpg.ScrollIntoView(target);
            _window.ListEpg.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _window.ListEpg.ScrollIntoView(target);
                }
                catch { }
                finally { _ignoreAutoScrollEvents = false; }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool ShouldCenterTarget(EpgProgram target, string channelKey)
        {
            if (target == null) return false;
            if (!string.Equals(_lastCenteredChannelKey, channelKey ?? "", StringComparison.OrdinalIgnoreCase)
                || _lastCenteredStart != target.Start
                || _lastCenteredEnd != target.End)
            {
                _lastCenteredChannelKey = channelKey ?? "";
                _lastCenteredStart = target.Start;
                _lastCenteredEnd = target.End;
                return true;
            }
            return false;
        }

        private ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer sv) return sv;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        public void ListEpg_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBoxItem item && item.DataContext is EpgProgram prog)
            {
                if ((prog.Status == "回放" || prog.Status == ResxLocalizer.Get("EPG_Status_Playback", "回放")) && _shell.CurrentChannel != null)
                {
                    _shell.ChannelPlaybackActions.PlayCatchup(_shell.CurrentChannel, prog);
                    e.Handled = true;
                }
            }
        }

        public void EpgMenu_Remind_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_shell.CurrentChannel == null) return;
                var mi = sender as System.Windows.Controls.MenuItem;
                if (mi == null) return;
                var ctx = mi.DataContext as EpgProgram;
                if (ctx == null) return;
                var now = DateTime.Now;
                if (!_shell.EpgReminderActions.CanCreateReminder(ctx, now)) return;
                var ownerWin = (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null) ? (Window)_shell.WindowStateActions.FullscreenWindow : _window;
                var dlg = new ReminderDialog(_shell.CurrentChannel.Name, ctx.Title, ctx.Start) { Owner = ownerWin, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = _shell.WindowStateActions.IsFullscreen };
                if (dlg.ShowDialog() == true)
                {
                    var r = _shell.EpgReminderActions.BuildReminder(_shell.CurrentChannel, ctx, dlg.PreAlertSeconds, dlg.Action, null);
                    _shell.EpgReminderActions.SaveReminder(r);
                    _shell.EpgReminderActions.NotifyReminder(r, _shell.CurrentChannel?.Logo);
                }
            }
            catch { }
        }

        public void EpgRemindButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_shell.CurrentChannel == null) return;
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is EpgProgram ctx)
                {
                    if (!_shell.EpgReminderActions.CanCreateReminder(ctx, DateTime.Now)) return;
                    var oldBooked = ctx.IsBooked;
                    ctx.IsBooked = true;
                    try { _window.ListEpg.Items.Refresh(); } catch { }
                    var ownerWin = (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null) ? (Window)_shell.WindowStateActions.FullscreenWindow : _window;
                    var dlg = new ReminderDialog(_shell.CurrentChannel.Name, ctx.Title, ctx.Start) { Owner = ownerWin, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = _shell.WindowStateActions.IsFullscreen };
                    if (dlg.ShowDialog() == true)
                    {
                        var r = _shell.EpgReminderActions.BuildReminder(_shell.CurrentChannel, ctx, dlg.PreAlertSeconds, dlg.Action, dlg.PlayMode);
                        _shell.EpgReminderActions.SaveReminder(r);
                        _shell.EpgReminderActions.NotifyReminder(r, _shell.CurrentChannel?.Logo);
                    }
                    else
                    {
                        ctx.IsBooked = oldBooked;
                        try { _window.ListEpg.Items.Refresh(); } catch { }
                    }
                }
            }
            catch { }
        }

        private List<EpgProgram> GeneratePlaceholderEpg()
        {
            var list = new List<EpgProgram>();
            var today = DateTime.Today;
            for (int i = 0; i < 24; i++)
            {
                var start = today.AddHours(i);
                var end = today.AddHours(i + 1);
                list.Add(new EpgProgram
                {
                    Title = ResxLocalizer.Get("EPG_Featured", "精彩节目"),
                    Start = start,
                    End = end
                });
            }
            return list;
        }
    }
}
