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
    OpenDebug
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

    public MainWindowShortcutAction ResolveAction(Key key)
    {
        // 根据播放状态决定 Left/Right 的行为
        bool isTimeshiftMode = _shell.IsTimeshiftActive || _shell.CurrentPlayingProgram != null;

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

    void TrySeekTimeshift(MainShellViewModel shell, int seconds)
    {
        if (shell.CurrentChannel == null || shell.PlayerEngine == null)
        {
            Diagnostics.Logger.Warn("[Seek] CurrentChannel or PlayerEngine is null");
            return;
        }

        var currentPos = shell.PlayerEngine.GetTimePos() ?? 0;
        var currentTime = shell.TimeshiftMin.AddSeconds(shell.TimeshiftCursorSec);
        var targetTime = currentTime.AddSeconds(seconds);
        var minTime = shell.TimeshiftMin;
        var maxTime = shell.TimeshiftMax;

        Diagnostics.Logger.Info($"[Seek] Timeshift seek: currentPos={currentPos}, seconds={seconds}, currentTime={currentTime:HH:mm:ss}, targetTime={targetTime:HH:mm:ss}");

        if (targetTime < minTime || targetTime > maxTime)
        {
            Diagnostics.Logger.Info($"[Seek] Target time out of timeshift range, reloading URL");
            shell.ChannelPlaybackActions.PlayCatchupAt(shell.CurrentChannel, targetTime);
            return;
        }

        if (shell.CurrentPlayingProgram != null &&
            targetTime >= shell.CurrentPlayingProgram.Start &&
            targetTime < shell.CurrentPlayingProgram.End)
        {
            Diagnostics.Logger.Info($"[Seek] Target time within current program, seeking");
            shell.PlayerEngine.SeekRelative(seconds);
            return;
        }

        Diagnostics.Logger.Info($"[Seek] Target time outside current program, reloading URL");
        shell.ChannelPlaybackActions.PlayCatchupAt(shell.CurrentChannel, targetTime);
    }
}
