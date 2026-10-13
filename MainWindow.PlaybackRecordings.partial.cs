using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

namespace LibmpvIptvClient
{
    public partial class MainWindow : Window
    {
        void OnTick(object? sender, EventArgs e)
        {
            _playbackTickManager?.Timer_Tick(sender, e);
            _playbackTickManager?.OnTick(sender, e);
        }
        /// <summary>Windowed timeline: the user grabbed the meter (see <see cref="Architecture.Presentation.View.MainWindowSeekInteractionManager"/>).</summary>
        void TimelineWindowed_SeekStart()
        {
            _seekInteractionManager?.Timeline_SeekStart();
        }

        /// <summary>Windowed timeline: the user released the meter; the payload is in the meter's own units.</summary>
        void TimelineWindowed_SeekEnd(double seconds)
        {
            _seekInteractionManager?.Timeline_SeekEnd(seconds);
        }

        /// <summary>
        /// Windowed bar segment click. Both bars route here through <see cref="ActivateTimelineSegment"/>, so the
        /// windowed and fullscreen timelines can never disagree about what a click means.
        /// </summary>
        void TimelineWindowed_ProgrammeSelected(TimelineProgramme programme)
        {
            var timeline = TimelineWindowed;
            ActivateTimelineSegment(programme, timeline?.ResolveSegment(programme) ?? default);
        }

        /// <summary>Windowed bar ruler click: the ruler is a time picker, so it takes the shared time path.</summary>
        void TimelineWindowed_TimeSeekRequested(DateTime time)
        {
            ActivateTimelineTime(time);
        }

        /// <summary>
        /// The one place a timeline segment click turns into playback, shared by the windowed bottom bar and the
        /// fullscreen overlay. Order: the live slot returns to the live edge, a slot inside the running timeshift
        /// buffer seeks that buffer, anything else still replayable starts catchup playback, and a future slot
        /// does nothing (its badge already announced the booking).
        /// </summary>
        internal void ActivateTimelineSegment(TimelineProgramme? programme, Services.TimelineSegmentVerdict verdict)
        {
            try
            {
                var channel = _shell.CurrentChannel;
                if (channel == null || programme == null) return;

                var target = verdict.Target == default(DateTime) ? programme.Start : verdict.Target;
                Diagnostics.Logger.Info($"节目单跳转 {verdict.Action} -> {target:yyyy-MM-dd HH:mm:ss}");

                switch (verdict.Action)
                {
                    case Services.TimelineSegmentAction.GoLive:
                        // Leave the buffer and follow the live edge again. The shell owns that transition, so
                        // mirroring it here is enough for both bars.
                        if (_shell.IsTimeshiftActive) _shell.IsTimeshiftActive = false;
                        else _shell.ChannelPlaybackActions.PlayChannel(channel);
                        break;

                    case Services.TimelineSegmentAction.SeekTimeshift:
                        _shell.TimeshiftCursorSec = Math.Max(0, (target - _shell.TimeshiftMin).TotalSeconds);
                        _shell.TimeshiftStart = target;
                        _shell.ChannelPlaybackActions.PlayCatchupAt(channel, target, keepTimeshiftMode: true);
                        break;

                    case Services.TimelineSegmentAction.PlayReplay:
                        _shell.ChannelPlaybackActions.PlayCatchupAt(channel, target, keepTimeshiftMode: false);
                        break;
                }
            }
            catch { }
        }

        /// <summary>
        /// A click on the ruler row, shared by both bars. In the live state this is the "open timeshift here"
        /// gesture: the shell is switched into timeshift first (the same path as ticking the overlay checkbox),
        /// then the buffer jumps onto the clicked wall clock. A channel that cannot timeshift falls back to
        /// catchup playback when the time is still inside the replay window, and is left alone otherwise.
        /// </summary>
        internal void ActivateTimelineTime(DateTime target)
        {
            try
            {
                var channel = _shell.CurrentChannel;
                if (channel == null) return;

                if (!_shell.IsTimeshiftActive) _shell.IsTimeshiftActive = true;

                if (_shell.IsTimeshiftActive)
                {
                    var clamped = target;
                    if (clamped < _shell.TimeshiftMin) clamped = _shell.TimeshiftMin;
                    if (_shell.TimeshiftMax > _shell.TimeshiftMin && clamped > _shell.TimeshiftMax) clamped = _shell.TimeshiftMax;

                    Diagnostics.Logger.Info($"时间轴跳转 {clamped:yyyy-MM-dd HH:mm:ss}");
                    _shell.TimeshiftCursorSec = Math.Max(0, (clamped - _shell.TimeshiftMin).TotalSeconds);
                    _shell.TimeshiftStart = clamped;
                    _shell.ChannelPlaybackActions.PlayCatchupAt(channel, clamped, keepTimeshiftMode: true);
                    return;
                }

                // No timeshift buffer on this source: a still-replayable instant becomes ordinary catchup playback.
                if (target < DateTime.Now && target > Services.ArchiveWindow.ReplayFloor)
                {
                    Diagnostics.Logger.Info($"时间轴跳转（回看）{target:yyyy-MM-dd HH:mm:ss}");
                    _shell.ChannelPlaybackActions.PlayCatchupAt(channel, target, keepTimeshiftMode: false);
                    return;
                }

                Diagnostics.Logger.Info($"时间轴跳转被忽略：{target:yyyy-MM-dd HH:mm:ss} 不支持时移且不可回看");
            }
            catch { }
        }

        void ClearIndicatorsOnStop()
        {
            try
            {
                ClearLiveAndEpgIndicators();
            }
            catch { }
            try
            {
                _shell.ClearRecordingPlayingIndicator();
            }
            catch { }
            try
            {
                _shell.DisableTimeshiftForStop();
                try { _overlayManager.OverlayWpf?.SetTimeshift(false); } catch { }
                try { if (CbTimeshift != null) CbTimeshift.IsChecked = false; } catch { }
                _shell.IsSpeedEnabled = false;
                _shell.DispatchPlaybackEvent(new LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.StopPlayback(false));
                _shell.CurrentUrl = "";
            }
            catch { }
        }
        void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_shell.PlaybackActions.TryTogglePlayPause(_shell.PlayerEngine, _shell.IsPaused, out var next))
            {
                _shell.IsPaused = next;
                UpdatePlayPauseIconFromManager();
            }
        }
        void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_shell.PlaybackActions.TryStop(_shell.PlayerEngine)) return;
            _shell.IsPaused = false;
            ClearIndicatorsOnStop();

            try
            {
                PlaceholderPanel.Visibility = Visibility.Visible;
                VideoHost.Visibility = Visibility.Collapsed;
            }
            catch { }
        }
        void BtnRew_Click(object sender, RoutedEventArgs e)
        {
            _shell.ShortcutActions.SeekBySeconds(-10);
        }
        void BtnFwd_Click(object sender, RoutedEventArgs e)
        {
            _shell.ShortcutActions.SeekBySeconds(10);
        }

        void CbFullscreen_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = false;
            if (sender is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                isChecked = tb.IsChecked == true;
            }
            ToggleFullscreen(_shell.ViewToggleActions.ResolveFullscreenTarget(isChecked));
        }
        void CbDrawer_Click(object sender, RoutedEventArgs e)
        {
            SetDrawerCollapsed(_shell.ViewToggleActions.ResolveDrawerCollapsed(CbDrawer.IsChecked));
        }

        void ClearLiveAndEpgIndicators()
        {
            try
            {
                var syncResult = _shell.ChannelPlaybackSyncActions.ClearLiveAndEpgIndicators(
                    _shell.Channels,
                    _shell.CurrentPlayingProgram,
                    ListEpg?.ItemsSource as IEnumerable<EpgProgram>,
                    _shell.EpgSelectionSyncActions.ClearPlaying);
                _shell.CurrentPlayingProgram = syncResult.CurrentPlayingProgram;
                if (syncResult.ShouldRefreshEpg)
                {
                    try { if (ListEpg?.Items != null) ListEpg.Items.Refresh(); } catch { }
                }
            }
            catch { }
        }
        string ResolveProgramTitleNow()
        {
            try
            {
                var now = DateTime.Now;
                if (_shell.CurrentChannel != null)
                {
                    var progs = _epgService?.GetPrograms(_shell.CurrentChannel.TvgId, _shell.CurrentChannel.TvgName, _shell.CurrentChannel.Name);
                    var p = progs?.FirstOrDefault(x => x.Start <= now && x.End > now);
                    if (p != null && !string.IsNullOrWhiteSpace(p.Title)) return p.Title!;
                }
            }
            catch { }
            return "";
        }

        void TxtRecordingsSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try { _recordingManager?.TxtRecordingsSearch_TextChanged(sender, e); } catch { }
        }
        void BtnRecordingsSearchClear_Click(object sender, RoutedEventArgs e)
        {
            try { _recordingManager?.BtnRecordingsSearchClear_Click(sender, e); } catch { }
        }
        void BtnChannelsRefresh_Click(object sender, RoutedEventArgs e)
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

        System.Collections.Generic.HashSet<string> _recordingsExpanded = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        void RecordingGroup_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Expander ex && ex.DataContext is LibmpvIptvClient.Services.RecordingGroup g)
                {
                    ex.IsExpanded = _recordingsExpanded.Contains(g.Channel ?? "");
                }
            }
            catch { }
        }
        void RecordingGroup_Expanded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Expander ex && ex.DataContext is LibmpvIptvClient.Services.RecordingGroup g)
                {
                    _recordingsExpanded.Add(g.Channel ?? "");
                }
            }
            catch { }
        }
        void RecordingGroup_Collapsed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Expander ex && ex.DataContext is LibmpvIptvClient.Services.RecordingGroup g)
                {
                    _recordingsExpanded.Remove(g.Channel ?? "");
                }
            }
            catch { }
        }

        void BtnRecordNow_Click(object sender, RoutedEventArgs e)
        {
            try { _recordingManager?.ToggleRecordNow(); } catch { }
        }

        void BtnRecordingsRefresh_Click(object sender, RoutedEventArgs e)
        {
            try { _recordingManager?.BtnRecordingsRefresh_Click(sender, e); } catch { }
        }

        void BtnRecordingPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is LibmpvIptvClient.Services.RecordingEntry item)
                {
                    _shell.RecordingPlaybackActions.PlayRecording(item, ListEpg.ItemsSource as IEnumerable<EpgProgram>);
                }
            }
            catch { }
        }

        void RecordingItem_ContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is LibmpvIptvClient.Services.RecordingEntry item)
                {
                    _recordingManager?.ShowContextMenu(fe, item);
                    e.Handled = true;
                }
            }
            catch { }
        }

        void BtnRecordingDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is LibmpvIptvClient.Services.RecordingEntry item)
                {
                    _recordingManager?.DownloadRecording(item);
                }
            }
            catch { }
        }

        void BtnRecordingUpload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is LibmpvIptvClient.Services.RecordingEntry item)
                {
                    _recordingManager?.UploadRecording(item);
                }
            }
            catch { }
        }

        void BtnRecordingUpload_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is LibmpvIptvClient.Services.RecordingEntry item)
                {
                    var label = (item.SourceLabel ?? "").ToLowerInvariant();
                    var hasRemote = label.Contains("网络") || label.Contains("remote") || label.Contains("удал");
                    btn.IsEnabled = !hasRemote;
                }
            }
            catch { }
        }

        public string? GetCurrentChannelId() => _shell.CurrentChannel?.TvgId ?? _shell.CurrentChannel?.Id;
        public string? GetCurrentChannelName() => _shell.CurrentChannel?.Name;
        public void SetFullscreenMode(bool on) => ToggleFullscreen(on);
    }
}
