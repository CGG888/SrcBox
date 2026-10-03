using LibmpvIptvClient.Controls;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed class SettingsEpgDrawerViewModel : ViewModelBase
{
    public EpgConfig BuildTempConfig(EpgConfig? source)
    {
        var config = new EpgConfig();
        config.CopyFrom(source);   // copies the additional Urls list too (issue #38)
        return config;
    }

    public void LoadDrawer(EpgDrawer? drawer, EpgConfig config)
    {
        drawer?.Load(config);
    }

    public void SaveDrawer(EpgDrawer? drawer, EpgConfig config)
    {
        drawer?.Save(config);
    }
}
