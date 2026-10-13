using System.Globalization;
using LibmpvIptvClient;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed record SettingsPlaybackFormState(
    string Decoder,
    string CacheSecs,
    string MaxBytes,
    string MaxBackBytes,
    string FccPrefetch,
    string SourceTimeout,
    bool EnableProtocolAdaptive,
    bool HlsStartAtLiveEdge,
    string HlsReadahead,
    string Alang,
    string Slang,
    string MpvNetworkTimeout,
    bool RememberPlaylistSource,
    bool RememberChannel,
    bool RememberChannelSource,
    SeekBarStyle SeekBar);

public sealed class SettingsPlaybackViewModel : ViewModelBase
{
    public SettingsPlaybackFormState BuildFormState(PlaybackSettings settings)
    {
        return new SettingsPlaybackFormState(
            settings.Decoder,
            settings.CacheSecs.ToString(CultureInfo.InvariantCulture),
            settings.DemuxerMaxBytesMiB.ToString(CultureInfo.InvariantCulture),
            settings.DemuxerMaxBackBytesMiB.ToString(CultureInfo.InvariantCulture),
            settings.FccPrefetchCount.ToString(CultureInfo.InvariantCulture),
            settings.SourceTimeoutSec.ToString(CultureInfo.InvariantCulture),
            settings.EnableProtocolAdaptive,
            settings.HlsStartAtLiveEdge,
            settings.HlsReadaheadSecs.ToString(CultureInfo.InvariantCulture),
            settings.Alang ?? string.Empty,
            settings.Slang ?? string.Empty,
            settings.MpvNetworkTimeoutSec.ToString(CultureInfo.InvariantCulture),
            settings.AutoLoadLastSource,
            settings.RememberChannelPerPlaylist,
            settings.RememberChannelSource,
            settings.SeekBar
        );
    }

    public void UpdateSettings(PlaybackSettings settings, SettingsPlaybackFormState form)
    {
        settings.Decoder = form.Decoder;
        
        if (double.TryParse(form.CacheSecs, NumberStyles.Any, CultureInfo.InvariantCulture, out var cache)) 
            settings.CacheSecs = Math.Max(0, cache);
        
        if (int.TryParse(form.MaxBytes, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxb)) 
            settings.DemuxerMaxBytesMiB = Math.Max(1, maxb);
        
        if (int.TryParse(form.MaxBackBytes, NumberStyles.Any, CultureInfo.InvariantCulture, out var backb)) 
            settings.DemuxerMaxBackBytesMiB = Math.Max(0, backb);
        
        if (int.TryParse(form.FccPrefetch, NumberStyles.Any, CultureInfo.InvariantCulture, out var pf)) 
            settings.FccPrefetchCount = Math.Max(0, Math.Min(3, pf));
        
        if (int.TryParse(form.SourceTimeout, NumberStyles.Any, CultureInfo.InvariantCulture, out var st)) 
            settings.SourceTimeoutSec = Math.Max(1, st);
            
        settings.EnableProtocolAdaptive = form.EnableProtocolAdaptive;
        settings.HlsStartAtLiveEdge = form.HlsStartAtLiveEdge;
        
        if (double.TryParse(form.HlsReadahead, NumberStyles.Any, CultureInfo.InvariantCulture, out var ra)) 
            settings.HlsReadaheadSecs = Math.Max(0, ra);
            
        settings.Alang = (form.Alang ?? string.Empty).Trim();
        settings.Slang = (form.Slang ?? string.Empty).Trim();
        
        if (int.TryParse(form.MpvNetworkTimeout, NumberStyles.Any, CultureInfo.InvariantCulture, out var nt)) 
            settings.MpvNetworkTimeoutSec = Math.Max(0, nt);

        settings.AutoLoadLastSource = form.RememberPlaylistSource;
        settings.RememberChannelPerPlaylist = form.RememberChannel;
        settings.RememberChannelSource = form.RememberChannelSource;

        // An unknown value can only arrive from a hand-edited settings file; fall back to the default timeline.
        settings.SeekBar = form.SeekBar == SeekBarStyle.Standard ? SeekBarStyle.Standard : SeekBarStyle.EpgTimeline;
    }
}
