using System;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

public sealed record OverlayBindingContext(
    Action? PlayPause,
    Action? Stop,
    Action? Rew,
    Action? Fwd,
    Action<string>? AspectRatioChanged,
    Action<double>? SpeedSelected,
    Action? PreviewRequested,
    Action<bool>? DrawerToggled,
    Action<bool>? EpgToggled,
    Action? SeekStart,
    Action<double>? SeekEnd,
    Action<double>? VolumeChanged,
    Action<bool>? MuteChanged,
    Action? SourceMenuRequested,
    Action<bool>? TimeshiftToggled,
    Func<System.Windows.Input.Key, bool>? ShortcutKeyPressed);

public sealed record TopOverlayBindingContext(
    Action? Minimize,
    Action? MaximizeRestore,
    Action? CloseWindow,
    Action? ExitApp,
    Action? FullscreenToggle,
    Action? OpenFile,
    Action? OpenUrl,
    Action? OpenSettings,
    Action? AddM3u,
    Action? AddM3uFile,
    Action<M3uSource>? LoadM3u,
    Action<M3uSource>? EditM3u,
    Action<bool>? FccChanged,
    Action<bool>? UdpChanged,
    Action<bool>? EpgToggled,
    Action<bool>? DrawerToggled,
    Action<bool>? MinimalModeChanged,
    Action<bool>? DeinterlaceChanged,
    Action<bool>? TopmostChanged,
    Func<bool>? IsUdpEnabled,
    Func<bool>? IsTopmost,
    Func<bool>? IsEpgVisible,
    Func<bool>? IsDrawerVisible,
    Func<bool>? IsMinimalMode,
    Func<System.Windows.Input.Key, bool>? ShortcutKeyPressed);

public sealed class MainWindowOverlayBindingActionsViewModel : ViewModelBase
{
    public void BindOverlay(OverlayControls overlay, OverlayBindingContext context)
    {
        if (context.PlayPause != null) overlay.PlayPause += context.PlayPause;
        if (context.Stop != null) overlay.Stop += context.Stop;
        if (context.Rew != null) overlay.Rew += context.Rew;
        if (context.Fwd != null) overlay.Fwd += context.Fwd;
        if (context.AspectRatioChanged != null) overlay.AspectRatioChanged += context.AspectRatioChanged;
        if (context.SpeedSelected != null) overlay.SpeedSelected += context.SpeedSelected;
        if (context.PreviewRequested != null) overlay.PreviewRequested += context.PreviewRequested;
        if (context.DrawerToggled != null) overlay.DrawerToggled += context.DrawerToggled;
        if (context.EpgToggled != null) overlay.EpgToggled += context.EpgToggled;
        if (context.SeekStart != null) overlay.SeekStart += context.SeekStart;
        if (context.SeekEnd != null) overlay.SeekEnd += context.SeekEnd;
        if (context.VolumeChanged != null) overlay.VolumeChanged += context.VolumeChanged;
        if (context.MuteChanged != null) overlay.MuteChanged += context.MuteChanged;
        if (context.SourceMenuRequested != null) overlay.SourceMenuRequested += context.SourceMenuRequested;
        if (context.TimeshiftToggled != null) overlay.TimeshiftToggled += context.TimeshiftToggled;
        if (context.ShortcutKeyPressed != null) overlay.ShortcutKeyPressed += context.ShortcutKeyPressed;
    }

    public void BindTopOverlay(TopOverlay topOverlay, TopOverlayBindingContext context)
    {
        if (context.Minimize != null) topOverlay.Minimize += context.Minimize;
        if (context.MaximizeRestore != null) topOverlay.MaximizeRestore += context.MaximizeRestore;
        if (context.CloseWindow != null) topOverlay.CloseWindow += context.CloseWindow;
        if (context.ExitApp != null) topOverlay.ExitApp += context.ExitApp;
        if (context.FullscreenToggle != null) topOverlay.FullscreenToggle += context.FullscreenToggle;
        if (context.OpenFile != null) topOverlay.OpenFile += context.OpenFile;
        if (context.OpenUrl != null) topOverlay.OpenUrl += context.OpenUrl;
        if (context.OpenSettings != null) topOverlay.OpenSettings += context.OpenSettings;
        if (context.AddM3u != null) topOverlay.AddM3u += context.AddM3u;
        if (context.AddM3uFile != null) topOverlay.AddM3uFile += context.AddM3uFile;
        if (context.LoadM3u != null) topOverlay.LoadM3u += context.LoadM3u;
        if (context.EditM3u != null) topOverlay.EditM3u += context.EditM3u;
        if (context.FccChanged != null) topOverlay.FccChanged += context.FccChanged;
        if (context.UdpChanged != null) topOverlay.UdpChanged += context.UdpChanged;
        if (context.EpgToggled != null) topOverlay.EpgToggled += context.EpgToggled;
        if (context.DrawerToggled != null) topOverlay.DrawerToggled += context.DrawerToggled;
        if (context.MinimalModeChanged != null) topOverlay.MinimalModeChanged += context.MinimalModeChanged;
        if (context.TopmostChanged != null) topOverlay.TopmostChanged += context.TopmostChanged;
        topOverlay.IsUdpEnabled = context.IsUdpEnabled;
        topOverlay.IsTopmost = context.IsTopmost;
        topOverlay.IsEpgVisible = context.IsEpgVisible;
        topOverlay.IsDrawerVisible = context.IsDrawerVisible;
        topOverlay.IsMinimalMode = context.IsMinimalMode;
        if (context.ShortcutKeyPressed != null) topOverlay.ShortcutKeyPressed += context.ShortcutKeyPressed;
    }
}
