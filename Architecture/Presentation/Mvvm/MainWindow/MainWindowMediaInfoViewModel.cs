using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LibmpvIptvClient.Architecture.Application.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowMediaInfoViewModel : ViewModelBase
    {
        private readonly MainShellViewModel _shell;
        public ObservableCollection<string> Tags { get; } = new();

        private string _infoText = "";
        public string InfoText
        {
            get => _infoText;
            set => SetProperty(ref _infoText, value);
        }

        public MainWindowMediaInfoViewModel(MainShellViewModel shell)
        {
            _shell = shell;
        }

        public void Update(IPlayerEngine? engine)
        {
            if (engine == null) return;

            try
            {
                var w = (int)(engine.GetPropertyDouble("width") ?? 0);
                var h = (int)(engine.GetPropertyDouble("height") ?? 0);
                var fps = engine.GetPropertyDouble("estimated-vf-fps") ?? engine.GetPropertyDouble("fps");
                var hw = engine.GetPropertyString("hwdec-current");
                var vcodec = engine.GetPropertyString("video-codec");
                var acodec = engine.GetPropertyString("audio-codec");
                
                // Bitrate calculation
                var brKbit = engine.GetPropertyDouble("video-params/bitrate");
                double? brRaw = engine.GetPropertyDouble("demuxer-bitrate") ?? engine.GetPropertyDouble("video-bitrate");
                string brStr = "-";

                if (brKbit.HasValue && brKbit.Value > 0)
                {
                    var mb = brKbit.Value / 8000.0; // kbit/s -> MB/s
                    brStr = $"{mb:0.0}MB/s";
                }
                else if (brRaw.HasValue)
                {
                    double v = brRaw.Value;
                    double mbps;
                    if (v < 900)
                    {
                        mbps = v;
                    }
                    else if (v < 9000)
                    {
                        mbps = v / 1000.0;
                    }
                    else if (v < 2_000_000)
                    {
                        mbps = v / 8000.0;
                    }
                    else
                    {
                        mbps = v / 8_000_000.0;
                    }
                    
                    if (double.IsFinite(mbps) && mbps > 0)
                    {
                        if (mbps > 500) mbps = v / 8_000_000.0; // sanity check
                        brStr = $"{mbps:0.0}MB/s";
                    }
                    else
                    {
                        brStr = "-";
                    }
                }

                var newTags = new List<string>();
                newTags.Add(string.IsNullOrEmpty(hw) ? "SW" : "HW");

                if (!string.IsNullOrWhiteSpace(vcodec))
                {
                    var up = vcodec.ToUpperInvariant();
                    if (up.Contains("HEVC") || up.Contains("H265")) newTags.Add("HEVC");
                    else if (up.Contains("H264") || up.Contains("AVC")) newTags.Add("H.264");
                    else if (up.Contains("MPEG-1") || up.Contains("MPEG1")) newTags.Add("MPEG-1");
                    else if (up.Contains("MPEG-2") || up.Contains("MPEG2")) newTags.Add("MPEG-2");
                    else if (up.Contains("VP8")) newTags.Add("VP8");
                    else if (up.Contains("VP9")) newTags.Add("VP9");
                    else if (up.Contains("AV1")) newTags.Add("AV1");
                    else if (up.Contains("WMV")) newTags.Add("WMV");
                    else newTags.Add(up);
                }

                if (!string.IsNullOrWhiteSpace(acodec))
                {
                    var up = acodec.ToUpperInvariant();
                    if (up.Contains("E-AC-3") || up.Contains("EAC3")) newTags.Add("EAC3");
                    else if (up.Contains("AC-3") || up.Contains("AC3")) newTags.Add("AC3");
                    else if (up.Contains("MP3") || up.Contains("MPEG AUDIO LAYER 3")) newTags.Add("MP3");
                    else if (up.Contains("MP2") || up.Contains("MPEG AUDIO LAYER 2") || up.Contains("MPEG LAYER II")) newTags.Add("MP2");
                    else if (up.Contains("AAC")) newTags.Add("AAC");
                    else if (up.Contains("WMA")) newTags.Add("WMA");
                    else if (up.Contains("FLAC")) newTags.Add("FLAC");
                    else if (up.Contains("OPUS")) newTags.Add("OPUS");
                    else if (up.Contains("VORBIS")) newTags.Add("VORBIS");
                    else if (up.Contains("PCM")) newTags.Add("PCM");
                    else if (up.Contains("DTS")) newTags.Add("DTS");
                    else if (up.Contains("TRUEHD")) newTags.Add("TRUEHD");
                    else newTags.Add(up);
                }

                if (h >= 2160) newTags.Add("4K");
                else if (h >= 1080) newTags.Add("FHD");
                else if (h >= 720) newTags.Add("HD");
                else if (h > 0) newTags.Add("SD");

                if (fps.HasValue && fps.Value > 0) newTags.Add($"{fps.Value:0.##}fps");
                newTags.Add(brStr);

                // Update ObservableCollection if changed
                if (!Tags.SequenceEqual(newTags))
                {
                    Tags.Clear();
                    foreach (var t in newTags) Tags.Add(t);
                }

                InfoText = string.Join("  ", newTags);
            }
            catch { }
        }
    }
}
