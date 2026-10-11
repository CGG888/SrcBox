using System;
using LibmpvIptvClient.Architecture.Application.Settings;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

/// <summary>
/// Holds the "a newer release exists" state shown as a dot next to the SrcBox title button.
/// The check itself runs silently in the background (see MainWindowStartupManager).
/// </summary>
public sealed class MainWindowUpdateBadgeActionsViewModel : ViewModelBase
{
    /// <summary>Minimum delay between two background checks.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private bool _hasNewVersion;

    public bool HasNewVersion
    {
        get => _hasNewVersion;
        private set => SetProperty(ref _hasNewVersion, value);
    }

    public string LatestVersion { get; private set; } = "";
    public string DownloadUrl { get; private set; } = "";

    /// <summary>Raised when the user clicks the badge.</summary>
    public event Action? RequestOpenUpdateFlow;

    /// <summary>Raised after the badge state changed, so the overlays can resync.</summary>
    public event Action? Changed;

    public void OpenUpdateFlow() => RequestOpenUpdateFlow?.Invoke();

    /// <summary>
    /// Applies one check result. Only a newer release with metadata lights the badge.
    /// </summary>
    /// <returns>True when the badge was lit by this result.</returns>
    public bool Apply(UpdateCheckResult result)
    {
        if (result == null || !result.IsNewer || result.ReleaseInfo == null)
        {
            return false;
        }

        LatestVersion = result.ReleaseInfo.Version ?? "";
        DownloadUrl = result.ReleaseInfo.DownloadUrl ?? "";
        HasNewVersion = true;
        Changed?.Invoke();
        return true;
    }

    /// <summary>True when no check ran yet or the last one is older than <see cref="CheckInterval"/>.</summary>
    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc)
        => lastCheckUtc == null || nowUtc - lastCheckUtc.Value >= CheckInterval;
}
