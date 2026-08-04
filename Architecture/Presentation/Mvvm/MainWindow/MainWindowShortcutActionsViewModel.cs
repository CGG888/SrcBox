using System.Windows.Input;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

    public enum MainWindowShortcutAction
{
    None,
    TogglePlayPause,
    Stop,
    SeekBackward,
    SeekForward,
    NextChannel,
    PreviousChannel,
    NextSource,
    PreviousSource,
    ToggleMute,
    ToggleFullscreen,
    ToggleDrawer,
    ToggleEpg,
    OpenDebug,
    PreviousProgram,
    NextProgram
}

public sealed class MainWindowShortcutActionsViewModel : ViewModelBase
{
    private readonly MainShellViewModel _shell;

    public event System.Action? RequestDebugWindow;
    public event System.Action? RequestToggleDrawer;
    public event System.Action? RequestToggleEpg;

    public MainWindowShortcutActionsViewModel(MainShellViewModel shell)
    {
        _shell = shell;
    }

    public MainWindowShortcutAction ResolveAction(Key key, ModifierKeys modifiers)
    {
        bool isTimeshiftMode = _shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null;
        bool isCtrlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (isTimeshiftMode && isCtrlPressed)
        {
            return key switch
            {
                Key.Up => MainWindowShortcutAction.PreviousProgram,
                Key.Down => MainWindowShortcutAction.NextProgram,
                _ => MainWindowShortcutAction.None
            };
        }

        return key switch
        {
            Key.Space => MainWindowShortcutAction.TogglePlayPause,
            Key.S => MainWindowShortcutAction.Stop,
            Key.Left => isTimeshiftMode ? MainWindowShortcutAction.SeekBackward : MainWindowShortcutAction.PreviousSource,
            Key.Right => isTimeshiftMode ? MainWindowShortcutAction.SeekForward : MainWindowShortcutAction.NextSource,
            Key.Up => MainWindowShortcutAction.PreviousChannel,
            Key.Down => MainWindowShortcutAction.NextChannel,
            Key.M => MainWindowShortcutAction.ToggleMute,
            Key.Enter => MainWindowShortcutAction.ToggleFullscreen,
            Key.L => MainWindowShortcutAction.ToggleDrawer,
            Key.E => MainWindowShortcutAction.ToggleEpg,
            Key.F1 => MainWindowShortcutAction.OpenDebug,
            _ => MainWindowShortcutAction.None
        };
    }

    public void ExecuteAction(MainWindowShortcutAction action)
    {
        switch (action)
        {
            case MainWindowShortcutAction.TogglePlayPause:
                if (_shell.PlaybackActions.TryTogglePlayPause(_shell.PlayerEngine, _shell.IsPaused, out var next))
                {
                    _shell.IsPaused = next;
                }
                break;
            case MainWindowShortcutAction.Stop:
                if (_shell.PlaybackActions.TryStop(_shell.PlayerEngine))
                {
                    _shell.IsPaused = false;
                }
                break;
            case MainWindowShortcutAction.SeekBackward:
                if (_shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null)
                {
                    if (_shell.IsTimeshiftActive)
                    {
                        TrySeekTimeshift(_shell, -10);
                    }
                    else
                    {
                        _shell.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, -10);
                    }
                }
                break;
            case MainWindowShortcutAction.SeekForward:
                if (_shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null)
                {
                    if (_shell.IsTimeshiftActive)
                    {
                        TrySeekTimeshift(_shell, 10);
                    }
                    else
                    {
                        _shell.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, 10);
                    }
                }
                break;
            case MainWindowShortcutAction.NextChannel:
            case MainWindowShortcutAction.PreviousChannel:
                TrySwitchChannel(action == MainWindowShortcutAction.NextChannel);
                break;
            case MainWindowShortcutAction.NextSource:
                _shell.MenuActions.SwitchSourceCycle(true);
                break;
            case MainWindowShortcutAction.PreviousProgram:
                TrySwitchProgram(false);
                break;
            case MainWindowShortcutAction.NextProgram:
                TrySwitchProgram(true);
                break;
            case MainWindowShortcutAction.PreviousSource:
                _shell.MenuActions.SwitchSourceCycle(false);
                break;
            case MainWindowShortcutAction.ToggleMute:
                _shell.PlaybackActions.TryToggleMute(_shell.PlayerEngine, _shell.IsMuted, out var nextMuted);
                _shell.IsMuted = nextMuted;
                break;
            case MainWindowShortcutAction.ToggleFullscreen:
                RequestToggleFullscreen?.Invoke(!_shell.WindowStateActions.IsFullscreen);
                break;
            case MainWindowShortcutAction.ToggleDrawer:
                RequestToggleDrawer?.Invoke();
                break;
            case MainWindowShortcutAction.ToggleEpg:
                RequestToggleEpg?.Invoke();
                break;
            case MainWindowShortcutAction.OpenDebug:
                RequestDebugWindow?.Invoke();
                break;
        }
    }

    public event System.Action<bool>? RequestToggleFullscreen;

    void TrySwitchChannel(bool next)
    {
        var list = _shell.FilteredChannels;
        if (list == null || list.Count == 0) return;
        var current = _shell.CurrentChannel;
        if (current == null)
        {
            _shell.ChannelPlaybackActions.PlayChannel(list[0], null);
            return;
        }
        int idx = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == current)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            _shell.ChannelPlaybackActions.PlayChannel(list[0], null);
            return;
        }
        int targetIdx;
        if (next)
        {
            targetIdx = (idx + 1) % list.Count;
        }
        else
        {
            targetIdx = (idx - 1 + list.Count) % list.Count;
        }
        _shell.ChannelPlaybackActions.PlayChannel(list[targetIdx], null);
    }

    public void TrySwitchProgram(bool next)
    {
        if (_shell.CurrentChannel == null || _shell.PlayerEngine == null)
        {
            Diagnostics.Logger.Warn("[Program] CurrentChannel or PlayerEngine is null");
            return;
        }

        if (!_shell.IsTimeshiftActive)
        {
            Diagnostics.Logger.Info("[Program] Not in timeshift mode, ignored");
            return;
        }

        Diagnostics.Logger.Info("[Program] Switching programs in timeshift mode is disabled - program follows playback position automatically");
        return;

        var currentProgram = _shell.CurrentPlayingProgram;
        if (currentProgram == null)
        {
            Diagnostics.Logger.Warn("[Program] CurrentPlayingProgram is null");
            return;
        }

        var programs = _shell.EpgService?.GetPrograms(_shell.CurrentChannel.TvgId, _shell.CurrentChannel.TvgName, _shell.CurrentChannel.Name);
        if (programs == null || programs.Count == 0)
        {
            Diagnostics.Logger.Warn("[Program] No programs found");
            return;
        }

        EpgProgram? targetProgram = null;
        if (next)
        {
            var nextPrograms = programs.Where(p => p.Start >= currentProgram.End).OrderBy(p => p.Start);
            targetProgram = nextPrograms.FirstOrDefault();
            if (targetProgram != null)
            {
                Diagnostics.Logger.Info($"[Program] Switching to next: {targetProgram.Title} [{targetProgram.Start:HH:mm:ss}-{targetProgram.End:HH:mm:ss}]");
            }
        }
        else
        {
            var prevPrograms = programs.Where(p => p.End <= currentProgram.Start).OrderByDescending(p => p.End);
            targetProgram = prevPrograms.FirstOrDefault();
            if (targetProgram != null)
            {
                Diagnostics.Logger.Info($"[Program] Switching to previous: {targetProgram.Title} [{targetProgram.Start:HH:mm:ss}-{targetProgram.End:HH:mm:ss}]");
            }
        }

        if (targetProgram != null)
        {
            _shell.PlayerEngine.EnsureReadyForLoad();
            _shell.ChannelPlaybackActions.PlayCatchupAt(_shell.CurrentChannel, targetProgram.Start);
            _shell.TimeshiftStart = targetProgram.Start;
        }
        else
        {
            Diagnostics.Logger.Info($"[Program] No {(next ? "next" : "previous")} program found");
        }
    }

    void TrySeekTimeshift(MainShellViewModel shell, int seconds)
    {
        if (shell.CurrentChannel == null || shell.PlayerEngine == null)
        {
            Diagnostics.Logger.Warn("[Seek] CurrentChannel or PlayerEngine is null");
            return;
        }

        Diagnostics.Logger.Info($"[Seek] Timeshift seek: seconds={seconds}, currentCursor={shell.TimeshiftCursorSec}");
        Diagnostics.Logger.Info($"[Seek] Timeshift mode - seeking directly without program boundary limits");
        shell.PlayerEngine.SeekRelative(seconds);
        shell.TimeshiftCursorSec = Math.Max(0, shell.TimeshiftCursorSec + seconds);
        Diagnostics.Logger.Info($"[Seek] After seek, TimeshiftCursorSec updated to {shell.TimeshiftCursorSec}");
    }
}
