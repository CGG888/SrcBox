using LibmpvIptvClient.Architecture.Application.Settings;
using System.Threading;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed class SettingsUpdateViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private ReleaseInfo? _latestRelease;

    public string CurrentVersion { get; }

    public SettingsUpdateViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        CurrentVersion = _updateService.GetCurrentVersion();
    }

    public async Task<(bool IsNewer, ReleaseInfo? ReleaseInfo, string Message)> CheckUpdateAsync(bool interactive, CancellationToken cancellationToken = default)
    {
        var result = await _updateService.CheckForUpdatesAsync(interactive, cancellationToken);
        
        if (result.ReleaseInfo != null)
        {
            _latestRelease = result.ReleaseInfo;
        }

        if (!result.IsNewer)
        {
            return (false, null, result.ErrorMessage);
        }

        return (true, result.ReleaseInfo, string.Empty);
    }

    public ReleaseInfo? GetLatestRelease()
    {
        return _latestRelease;
    }
}
