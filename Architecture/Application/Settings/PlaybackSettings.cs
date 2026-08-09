using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibmpvIptvClient.Helpers;

namespace LibmpvIptvClient
{
    public static class DecoderOptions
    {
        public static readonly string[] AllDecoders = new[] { "auto", "d3d11va", "dxva2", "nvdec", "no" };
        public static string GetDisplayName(string decoder)
        {
            return decoder switch
            {
                "auto" => Localizer.S("Decoder_Auto", "Auto"),
                "d3d11va" => Localizer.S("Decoder_D3D11VA", "D3D11VA (HW)"),
                "dxva2" => Localizer.S("Decoder_DXVA2", "DXVA2 (HW)"),
                "nvdec" => Localizer.S("Decoder_NVDEC", "NVDEC (HW)"),
                "no" => Localizer.S("Decoder_Software", "Software"),
                _ => decoder
            };
        }
    }

    public class M3uSource
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    public class EpgConfig
    {
        public bool Enabled { get; set; } = true;
        public string Url { get; set; } = "";
        public double RefreshIntervalHours { get; set; } = 24;
        public bool EnableSmartMatch { get; set; } = true; // Added
        public bool StrictMatchByPlaybackTime { get; set; } = true; // 灰度开关：按回放/时移的播放时刻匹配节目单
    }

    public class LogoConfig
    {
        public bool Enabled { get; set; } = true;
        public string Url { get; set; } = "";
        public bool EnableCache { get; set; } = true;
        public string CacheDir { get; set; } = "";
        public double CacheTtlHours { get; set; } = 168;
        public int CacheMaxMiB { get; set; } = 500;
    }

    public class ReplayConfig
    {
        public bool Enabled { get; set; } = true;
        public string UrlFormat { get; set; } = "";
        public int DurationHours { get; set; } = 72;
        // 回放时追加 epg_time 参数（默认关闭，避免影响部分不支持的播放源）
        public bool AppendEpgTime { get; set; } = false;
    }

    public class TimeshiftConfig
    {
        public bool Enabled { get; set; } = true;
        public string UrlFormat { get; set; } = "";
        public int DurationHours { get; set; } = 6;
        // 时移时追加 epg_time 参数（默认关闭，避免影响部分不支持的播放源）
        public bool AppendEpgTime { get; set; } = false;
    }

    public class PlaybackSettings
    {
        public string Decoder { get; set; } = "auto";
        public double CacheSecs { get; set; } = 5.0;
        public int DemuxerMaxBytesMiB { get; set; } = 16;
        public int DemuxerMaxBackBytesMiB { get; set; } = 4;
        public int FccPrefetchCount { get; set; } = 2;
        public bool EnableUdpOptimization { get; set; } = false; // Added
        public int SourceTimeoutSec { get; set; } = 5; // NEW-24: 3s was too short for some streams, increased to 5s

        // 反交错 (Deinterlace) - 针对 1080i/720i 隔行扫描流
        // 模式: "no"=关闭 / "yes"=强制 / "auto"=自动检测(推荐, 零副作用)
        public string Deinterlace { get; set; } = "auto";
        // 场序: "auto"=自动 / "tff"=顶场优先 / "bff"=底场优先
        public string DeinterlaceFieldParity { get; set; } = "auto";
        // 去隔行算法: "yadif"=默认(质量好) / "bwdif"=更优 / "none"=仅用 deinterlace flag 不挂 vf
        public string DeinterlaceAlgorithm { get; set; } = "yadif";
            
            // Adaptive per-protocol tuning
            public bool EnableProtocolAdaptive { get; set; } = true;
            // HLS specific
            public bool HlsStartAtLiveEdge { get; set; } = false;
            public double HlsReadaheadSecs { get; set; } = 0; // 0 = follow mpv default
            // Language preferences
            public string Alang { get; set; } = "";
            public string Slang { get; set; } = "";
            // mpv network timeout (seconds). 0 = keep default
            public int MpvNetworkTimeoutSec { get; set; } = 0;

            // 音频设置
            // 音量增益 (dB): -200 ~ +60 dB，默认 0
            public double VolumeGain { get; set; } = 0;
            // 最大音量 (%): 100 ~ 1000，默认 130
            public int VolumeMax { get; set; } = 130;
            // 初始音量 (0~100): 默认 50
            public double Volume { get; set; } = 50;
            // 音频延迟 (秒): -100 ~ 100，默认 0
            public double AudioDelay { get; set; } = 0;

        public EpgConfig Epg { get; set; } = new EpgConfig();
        public LogoConfig Logo { get; set; } = new LogoConfig();
        public ReplayConfig Replay { get; set; } = new ReplayConfig();
        public TimeshiftConfig Timeshift { get; set; } = new TimeshiftConfig();
        public HttpHeaderConfig HttpHeaders { get; set; } = new HttpHeaderConfig();
        
        public TimeOverrideConfig TimeOverride { get; set; } = new TimeOverrideConfig();
        public WebDavConfig WebDav { get; set; } = new WebDavConfig();
        public WebRemoteConfig WebRemote { get; set; } = new WebRemoteConfig();
        public string RecordingLocalDir { get; set; } = "recordings/{channel}";
        public RecordingConfig Recording { get; set; } = new RecordingConfig();

        // Compatibility Properties (Deprecated)
        [System.Text.Json.Serialization.JsonIgnore]
        public string CustomEpgUrl { get => Epg.Url; set => Epg.Url = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string CustomLogoUrl { get => Logo.Url; set => Logo.Url = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public int TimeshiftHours { get => Timeshift.DurationHours; set => Timeshift.DurationHours = value; }

        public List<M3uSource> SavedSources { get; set; } = new List<M3uSource>();
        // 更新下载 CDN 持久化列表（仅前缀，例如 https://gh-proxy.org）
        public List<string> UpdateCdnMirrors { get; set; } = new List<string>();
        // M3U 缓存设置
        public bool EnableM3uCache { get; set; } = true;
        public double M3uCacheTtlHours { get; set; } = 12;
        // 界面语言（例如 zh-CN / en-US；为空表示跟随系统）
        public string Language { get; set; } = "";
        // 主题模式：System/Light/Dark
        public string ThemeMode { get; set; } = "System";
        public string LastLocalM3uPath { get; set; } = "";
        public bool AutoLoadLastSource { get; set; } = true;
        // 频道快照预览（需要 rtp2httpd 开启 X-Request-Snapshot）
        public bool EnableChannelPreview { get; set; } = false;
        public List<ScheduledReminder> ScheduledReminders { get; set; } = new List<ScheduledReminder>();
        public bool ConfirmOnClose { get; set; } = true;
        public string CloseMode { get; set; } = "";
        public List<string> ChannelGroupOrder { get; set; } = new List<string>();
        public bool SkipShortcutsDialog { get; set; } = false;

        public static PlaybackSettings Load()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var obj = JsonSerializer.Deserialize<PlaybackSettings>(json);
                    if (obj != null) return obj;
                }
            }
            catch { }
            return new PlaybackSettings();
        }

        public void Save()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                try { LibmpvIptvClient.Diagnostics.Logger.Info("[Settings] 保存成功 user_settings.json"); } catch { }
            }
            catch { }
        }
    }

    public static class AppSettings
    {
        public static PlaybackSettings Current { get; set; } = PlaybackSettings.Load();
    }
    
    public class RecordingConfig
    {
        public bool Enabled { get; set; } = true;
        public string DefaultPlayChoice { get; set; } = "prompt";
        public string LastPlayChoice { get; set; } = "";
        public string SaveMode { get; set; } = "local_then_upload";
        public string DirTemplate { get; set; } = "recordings/{channel}";
        public string FileTemplate { get; set; } = "{yyyyMMdd_HHmmss}.ts";
        public bool VerifyDirReady { get; set; } = true;
        public int GrowthTimeoutSec { get; set; } = 20;
        public int RetryCount { get; set; } = 1;
        public int UploadMaxConcurrency { get; set; } = 1;
        public int UploadRetry { get; set; } = 3;
        public int UploadRetryBackoffMs { get; set; } = 1000;
        public int UploadMaxKBps { get; set; } = 0;
        public bool ResumeUpload { get; set; } = false;
        public int RealtimeUploadIntervalSec { get; set; } = 5;
        public string RemoteTempSuffix { get; set; } = ".part";
        public bool RealtimeFinalizeEnabled { get; set; } = false;
        public int RealtimeFinalizeDelaySec { get; set; } = 10;
        public int RealtimeFinalizeMaxKBps { get; set; } = 0;
        public int MaxBackgroundRecordings { get; set; } = 3;
    }
    
    public class WebDavConfig
    {
        public bool Enabled { get; set; } = false;
        public string BaseUrl { get; set; } = "";
        public string Username { get; set; } = "";
        [JsonIgnore]
        public string TokenOrPassword { get; set; } = "";
        public string EncryptedToken { get; set; } = "";
        public bool AllowSelfSignedCert { get; set; } = false;
        public string RootPath { get; set; } = "/srcbox/";
        public string RecordingsPath { get; set; } = "/srcbox/recordings/";
        public string UserDataPath { get; set; } = "/srcbox/user-data/";
        public bool? MoveSupported { get; set; }
        public bool? CopySupported { get; set; }
    }

    public class WebRemoteConfig
    {
        public bool Enabled { get; set; } = false;
        public int HttpPort { get; set; } = 8899;
        public bool RequirePassword { get; set; } = false;
        public string Password { get; set; } = "";
        public bool ShowChannelList { get; set; } = true;
        public bool ShowEpgList { get; set; } = true;
        public int MaxEpgItems { get; set; } = 20;
    }

    public class TimeOverrideConfig
    {
        public bool Enabled { get; set; } = false;
        public string Mode { get; set; } = "time_only";
        public string Layout { get; set; } = "start_end";
        public string Encoding { get; set; } = "local";
        public string StartKey { get; set; } = "start";
        public string EndKey { get; set; } = "end";
        public string DurationKey { get; set; } = "duration";
        public string PlayseekKey { get; set; } = "playseek";
        public bool UrlEncode { get; set; } = true;
    }

    public class HttpHeaderConfig
    {
        // HTTP/HTTPS 流 Header（多行文本）
        public string Headers { get; set; } = "";
        // RTSP User-Agent
        public string RtspUserAgent { get; set; } = "";
        // RTSP 用户名
        public string RtspUser { get; set; } = "";
        // RTSP 密码（加密存储）
        public string EncryptedRtspPassword { get; set; } = "";
        // RTSP 传输模式：tcp/udp/http
        public string RtspTransport { get; set; } = "tcp";
    }
    
    public class ScheduledReminder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ChannelId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string ChannelLogo { get; set; } = "";
        public DateTime StartAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime EndTimeUtc { get; set; } = DateTime.UtcNow.AddHours(1);
        public int PreAlertSeconds { get; set; } = 0;
        public string Action { get; set; } = "notify";
        public string? PlayMode { get; set; } = "default";
        public bool Enabled { get; set; } = true;
        public bool Completed { get; set; } = false;
        public string Note { get; set; } = "";
        public string? RecordMode { get; set; }
        public int? RecordDurationMin { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Selected { get; set; } = false;
    }
}
