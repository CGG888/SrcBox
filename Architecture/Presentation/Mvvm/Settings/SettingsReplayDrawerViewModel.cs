using LibmpvIptvClient.Controls;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed class SettingsReplayDrawerViewModel : ViewModelBase
{
    public ReplayConfig BuildTempConfig(ReplayConfig? source)
    {
        if (source == null)
        {
            return new ReplayConfig();
        }

        return new ReplayConfig
        {
            Enabled = source.Enabled,
            UrlFormat = source.UrlFormat,
            DurationHours = source.DurationHours,
            // The drawer edits this flag (Controls/PlaybackDrawer), so it has to survive the round trip.
            AppendEpgTime = source.AppendEpgTime
        };
    }

    public void LoadDrawer(PlaybackDrawer? drawer, ReplayConfig config)
    {
        drawer?.Load(config);
    }

    public void SaveDrawer(PlaybackDrawer? drawer, ReplayConfig config)
    {
        drawer?.Save(config);
    }
}
