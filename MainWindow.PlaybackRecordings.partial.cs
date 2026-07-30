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
        void SliderSeek_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seekInteractionManager?.SliderSeek_PreviewMouseDown(sender, e);
        }
        void SliderSeek_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seekInteractionManager?.SliderSeek_PreviewMouseUp(sender, e);
        }

        void SliderSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }
        void SliderSeek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _seekInteractionManager?.SliderSeek_MouseMove(sender, e);
        }
        void SliderSeek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _seekInteractionManager?.SliderSeek_MouseLeave(sender, e);
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
            _shell.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, -10);
        }
        void BtnFwd_Click(object sender, RoutedEventArgs e)
        {
            _shell.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, 10);
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
