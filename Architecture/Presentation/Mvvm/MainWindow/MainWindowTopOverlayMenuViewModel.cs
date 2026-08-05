using System;
using System.Windows.Controls;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

    public sealed class MainWindowTopOverlayMenuViewModel : ViewModelBase
    {
        public ContextMenu BuildMenu(
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
        string currentAspectRatio = "default",
        Action<string>? ratioChanged = null,
        double currentSpeed = 1.0,
        Action<double>? speedChanged = null)
    {
        LibmpvIptvClient.Helpers.MenuBuilder.SetCurrentAspectRatio(currentAspectRatio);
        if (ratioChanged != null)
            LibmpvIptvClient.Helpers.MenuBuilder.SetRatioCallback(ratioChanged);
        LibmpvIptvClient.Helpers.MenuBuilder.SetCurrentSpeed(currentSpeed);
        if (speedChanged != null)
            LibmpvIptvClient.Helpers.MenuBuilder.SetSpeedCallback(speedChanged);
        return LibmpvIptvClient.Helpers.MenuBuilder.BuildMainMenu(
            openFile: openFile,
            openUrl: openUrl,
            addM3uFile: addM3uFile,
            addM3uUrl: addM3uUrl,
            editM3u: editM3u,
            loadM3u: loadM3u,
            openSettings: openSettings,
            showAbout: showAbout,
            exitApp: exitApp,
            toggleFcc: toggleFcc,
            toggleUdp: toggleUdp,
            toggleEpg: toggleEpg,
            toggleDrawer: toggleDrawer,
            toggleMinimal: toggleMinimal,
            toggleDeinterlace: toggleDeinterlace,
            isEpgChecked: isEpgChecked,
            isDrawerChecked: isDrawerChecked,
            isMinimalChecked: isMinimalChecked,
            refreshChannels: refreshChannels,
            togglePlayPause: togglePlayPause,
            stopPlayback: stopPlayback,
            seekForward: seekForward,
            seekBackward: seekBackward,
            prevChannel: prevChannel,
            nextChannel: nextChannel,
            toggleMute: toggleMute,
            volumeUp: volumeUp,
            volumeDown: volumeDown,
            toggleTopmost: toggleTopmost,
            openDebug: openDebug,
            showShortcuts: showShortcuts,
            isTopmostChecked: isTopmostChecked);
    }
}