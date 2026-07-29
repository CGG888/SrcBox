using System;
using System.Runtime.InteropServices;
using System.Text;
using LibmpvIptvClient.Diagnostics;
using System.Globalization;

namespace LibmpvIptvClient
{
    public class MpvInterop : IDisposable
    {
        IntPtr _handle = IntPtr.Zero;
        PlaybackSettings _settings = AppSettings.Current;
        public void Create()
        {
            _handle = mpv_create();
        }
        public void SetSettings(PlaybackSettings s) { _settings = s; }
        public void Initialize()
        {
            mpv_initialize(_handle);
            SetFlag("keep-open", true);
            SetFlag("idle", true);
            SetString("ytdl", "no");
            SetString("hwdec", _settings.Hwdec ? "d3d11va" : "no");
            SetString("gpu-api", "d3d11");
            var threads = Math.Max(2, Environment.ProcessorCount / 2);
            SetString("vd-lavc-threads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // 确保音频输出正常
            SetString("mute", "no");
            SetString("audio", "yes");
            SetString("audio-device", "auto");
            SetString("ad-lavc-threads", "2");
            SetString("audio-channels", "stereo");
            SetString("ad-lavc-downmix", "yes");
            SetString("audio-pitch-correction", "yes");
            
            // 设置全局通用 User-Agent，解决部分源因空 UA 拒绝访问的问题
            SetString("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            // 忽略 HTTPS 证书错误，解决部分自签名源无法播放的问题
            SetString("tls-verify", "no");
            // 用户语言偏好与网络超时
            if (!string.IsNullOrWhiteSpace(_settings.Alang)) SetString("alang", _settings.Alang);
            if (!string.IsNullOrWhiteSpace(_settings.Slang)) SetString("slang", _settings.Slang);
            if (_settings.MpvNetworkTimeoutSec > 0) SetString("network-timeout", _settings.MpvNetworkTimeoutSec.ToString(System.Globalization.CultureInfo.InvariantCulture));


            // 反交错（针对 1080i/720i 隔行流）
            // 在 Initialize 阶段设置，对后续所有 loadfile 自动生效
            try
            {
                var diMode = string.IsNullOrWhiteSpace(_settings.Deinterlace) ? "no" : _settings.Deinterlace;
                SetString("deinterlace", diMode);
                if (!string.Equals(diMode, "no", StringComparison.OrdinalIgnoreCase))
                {
                    var diParity = string.IsNullOrWhiteSpace(_settings.DeinterlaceFieldParity) ? "auto" : _settings.DeinterlaceFieldParity;
                    SetString("deinterlace-field-parity", diParity);
                    var diAlgo = string.IsNullOrWhiteSpace(_settings.DeinterlaceAlgorithm) ? "yadif" : _settings.DeinterlaceAlgorithm;
                    if (!string.Equals(diAlgo, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        // yadif=mode=1: 无延迟；bwdif=mode=1: 无延迟
                        SetString("vf", $"{diAlgo}=mode=1");
                    }
                }

            }
            catch { /* 静默 */ }

            // 音频设置
            try
            {
                // volume-gain: 音量增益 (dB), -200 ~ +60
                var gain = Math.Max(-200, Math.Min(60, _settings.VolumeGain));
                SetString("volume-gain", gain.ToString(CultureInfo.InvariantCulture));
                // volume-max: 最大音量 (%), 100 ~ 1000
                var volMax = Math.Max(100, Math.Min(1000, _settings.VolumeMax));
                SetString("volume-max", volMax.ToString(CultureInfo.InvariantCulture));
                // audio-delay: 音频延迟 (秒), -100 ~ 100
                var delay = Math.Max(-100.0, Math.Min(100.0, _settings.AudioDelay));
                SetString("audio-delay", delay.ToString(CultureInfo.InvariantCulture));

            }
            catch { /* 静默 */ }

            var initDiMode = string.IsNullOrWhiteSpace(_settings.Deinterlace) ? "no" : _settings.Deinterlace;
            var initGain = Math.Max(-200, Math.Min(60, _settings.VolumeGain));
            Logger.Debug($"[mpv] init alang={_settings.Alang} slang={_settings.Slang} di={initDiMode} gain={initGain}dB");
        }
        public void SetSpeed(double speed)
        {
            SetDouble("speed", speed);
        }
        public void SetWid(IntPtr hwnd)
        {
            var prop = "wid";
            var val = BitConverter.GetBytes(hwnd.ToInt64());
            mpv_set_property(_handle, prop, mpv_format.MPV_FORMAT_INT64, val);
        }
        void SetFlag(string name, bool value)
        {
            var data = BitConverter.GetBytes(value ? 1 : 0);
            mpv_set_property(_handle, name, mpv_format.MPV_FORMAT_FLAG, data);
        }
        public void Mute(bool mute)
        {
            SetFlag("mute", mute);
        }
        public void SetString(string name, string value)
        {
            mpv_set_property_string(_handle, name, value);
        }
        public void SetOptionString(string name, string value)
        {
            mpv_set_property_string(_handle, name, value);
        }
        public void SetPropertyString(string name, string value)
        {
            mpv_set_property_string(_handle, name, value);
        }
        void SetupProtocolOptions(string url)
        {
            var u = url.ToLowerInvariant();

            // 1. RTSP Special Handling
            if (u.StartsWith("rtsp://"))
            {
                var headers = _settings.HttpHeaders;

                // RTSP 传输模式（默认 TCP）
                SetString("rtsp-transport", string.IsNullOrWhiteSpace(headers?.RtspTransport) ? "tcp" : headers.RtspTransport);

                // RTSP User-Agent（使用自定义或默认）
                var rtspUa = string.IsNullOrWhiteSpace(headers?.RtspUserAgent) ? "VLC/3.0.18Libmpv" : headers.RtspUserAgent;
                SetString("user-agent", rtspUa);

                // RTSP 认证
                if (!string.IsNullOrWhiteSpace(headers?.RtspUser))
                {
                    SetString("rtsp-user", headers.RtspUser);
                    if (!string.IsNullOrWhiteSpace(headers?.EncryptedRtspPassword))
                    {
                        var pwd = LibmpvIptvClient.Services.CryptoUtil.UnprotectString(headers.EncryptedRtspPassword);
                        SetString("rtsp-password", pwd);
                    }
                }

                // Enable cache for RTSP to allow smoother playback
                SetString("cache", _settings.CacheSecs > 0 ? "yes" : "no");
                if (_settings.CacheSecs > 0)
                    SetString("cache-secs", _settings.CacheSecs.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // RTSP doesn't need forced demuxer, let ffmpeg handle it
                SetString("demuxer-lavf-format", "");
                SetString("demuxer-lavf-probesize", "32"); // Fast probe
                SetString("demuxer-lavf-analyzeduration", "0");

                return;
            }

            // 2. TS / MPEG-TS / UDP Handling
            var looksTs = u.Contains("/rtp/") || u.EndsWith(".ts") || u.Contains("proto=http") || u.StartsWith("udp://");
            if (looksTs)
            {
                SetString("demuxer", "lavf");
                SetString("demuxer-lavf-format", "mpegts");
                SetString("demuxer-lavf-probesize", "32"); // 极小 probesize，加速首帧
                SetString("demuxer-lavf-analyzeduration", "0"); // 禁用分析时长，加速首帧
                SetString("demuxer-lavf-buffersize", "128000"); // 减小缓冲区
                if (u.StartsWith("udp://"))
                {
                    // 改为开启缓存以支持录制，通过 cache-secs 控制延迟
                    SetString("cache", "yes");
                    SetString("cache-secs", _settings.CacheSecs > 0 ? _settings.CacheSecs.ToString(System.Globalization.CultureInfo.InvariantCulture) : "10");
                    SetString("demuxer-max-back-bytes", "128MiB"); // 允许回溯

                }
                else
                {
                    SetString("cache", _settings.CacheSecs > 0 ? "yes" : "no");
                    if (_settings.CacheSecs > 0)
                        SetString("cache-secs", _settings.CacheSecs.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    SetString("demuxer-max-bytes", $"{_settings.DemuxerMaxBytesMiB}MiB");
                    SetString("demuxer-max-back-bytes", $"{_settings.DemuxerMaxBackBytesMiB}MiB");

                }
            }
            else
            {
                // 清空强制格式，恢复自动探测
                SetString("demuxer-lavf-format", "");
                
                // 强制开启激进缓存以支持录制
                SetString("cache", "yes");
                SetString("demuxer-max-bytes", "512MiB");
                SetString("demuxer-max-back-bytes", "256MiB");
                SetString("force-seekable", "yes"); // 关键：强制视为可回溯
                
                // 强制 demuxer 预读并保持数据，这对于 stream-record 至关重要
                SetString("demuxer-readahead-secs", "60"); 
                SetString("demuxer-cache-wait", "no"); // 不要等待缓存填满才播放，但要持续缓存
                

            }

            // 4. HTTP/HTTPS 自定义 Header（适用于所有 HTTP 流）
            if ((u.StartsWith("http://") || u.StartsWith("https://")) && !string.IsNullOrWhiteSpace(_settings.HttpHeaders?.Headers))
            {
                // mpv 格式：每行用 \n 分隔
                var headers = _settings.HttpHeaders.Headers.Replace("\r\n", "\n").Replace("\n", "\\n");
                SetString("http-header-fields", headers);

            }
            // 3. HLS 自适应（仅在启用时）
            if (_settings.EnableProtocolAdaptive && (u.Contains(".m3u8") || u.Contains("format=hls")))
            {
                if (_settings.HlsStartAtLiveEdge) SetString("hls-playlist-start", "no");
                if (_settings.HlsReadaheadSecs > 0)
                    SetString("demuxer-readahead-secs", _settings.HlsReadaheadSecs.ToString(CultureInfo.InvariantCulture));
            }

            var proto = u.StartsWith("rtsp://") ? "RTSP" :
                        u.StartsWith("udp://") ? "UDP" :
                        looksTs ? "TS" :
                        u.Contains(".m3u8") ? "HLS" : "HTTP";
            Logger.Debug($"[mpv] proto={proto} cache={_settings.CacheSecs > 0}");
        }
        public string? GetString(string name)
        {
            var p = mpv_get_property_string(_handle, name);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(p); }
            finally { mpv_free(p); }
        }
        public int? GetInt(string name)
        {
            var s = GetString(name);
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }
        public double? GetDouble(string name)
        {
            var s = GetString(name);
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }
        public void LoadFile(string url)
        {
            SetupProtocolOptions(url);
            var args = new string[] { "loadfile", url, null! };
            mpv_command(_handle, args);
            Logger.Log("mpv loadfile 调用完成");
        }
        public void LoadWithPrefetch(string url, string? nextUrl)
        {
            SetFlag("prefetch-playlist", true);
            var clear = new string[] { "playlist-clear", null! };
            mpv_command(_handle, clear);
            SetupProtocolOptions(url);
            var loadCurrent = new string[] { "loadfile", url, "replace", null! };
            mpv_command(_handle, loadCurrent);
            if (!string.IsNullOrWhiteSpace(nextUrl))
            {
                SetupProtocolOptions(nextUrl!);
                var loadNext = new string[] { "loadfile", nextUrl!, "append-play", null! };
                mpv_command(_handle, loadNext);
                Logger.Log("已预取下一频道");
            }
            Logger.Log("mpv loadfile + prefetch 调用完成");
        }
        public void LoadWithPrefetch(string url, System.Collections.Generic.IEnumerable<string> nextUrls)
        {
            SetFlag("prefetch-playlist", true);
            var clear = new string[] { "playlist-clear", null! };
            mpv_command(_handle, clear);
            SetupProtocolOptions(url);
            var loadCurrent = new string[] { "loadfile", url, "replace", null! };
            mpv_command(_handle, loadCurrent);
            foreach (var n in nextUrls)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                SetupProtocolOptions(n);
                var loadNext = new string[] { "loadfile", n, "append-play", null! };
                mpv_command(_handle, loadNext);
            }
            Logger.Log("播放加载完成");
        }
        public void Pause(bool pause)
        {
            SetFlag("pause", pause);
        }
        public void SeekRelative(double seconds)
        {
            var args = new string[] { "seek", seconds.ToString(CultureInfo.InvariantCulture), "relative", null! };
            mpv_command(_handle, args);
        }
        public void SeekAbsolute(double seconds)
        {
            var args = new string[] { "seek", seconds.ToString(CultureInfo.InvariantCulture), "absolute", null! };
            mpv_command(_handle, args);
        }
        public void SetVolume(double volume)
        {
            SetString("volume", volume.ToString(CultureInfo.InvariantCulture));
        }
        public void SetAspectRatio(string ratio)
        {
            // ratio: "default", "16:9", "4:3", "stretch", "fill", "crop"
            switch (ratio.ToLowerInvariant())
            {
                case "16:9":
                    SetString("video-aspect-override", "16:9");
                    SetString("keepaspect", "yes");
                    SetDouble("panscan", 0.0);
                    break;
                case "4:3":
                    SetString("video-aspect-override", "4:3");
                    SetString("keepaspect", "yes");
                    SetDouble("panscan", 0.0);
                    break;
                case "stretch":
                    SetString("video-aspect-override", "-1");
                    SetString("keepaspect", "no");
                    SetDouble("panscan", 0.0);
                    break;
                case "fill":
                    SetString("video-aspect-override", "-1");
                    SetString("keepaspect", "yes");
                    SetDouble("panscan", 1.0);
                    break;
                case "crop":
                    SetString("video-aspect-override", "-1");
                    SetString("keepaspect", "yes");
                    SetDouble("panscan", 1.0); // panscan=1.0 is crop/fill
                    break;
                default: // default
                    SetString("video-aspect-override", "-1");
                    SetString("keepaspect", "yes");
                    SetDouble("panscan", 0.0);
                    break;
            }
        }
        void SetDouble(string name, double value)
        {
            mpv_set_property(_handle, name, mpv_format.MPV_FORMAT_DOUBLE, BitConverter.GetBytes(value));
        }
        public void SetFullscreen(bool on)
        {
            SetFlag("fullscreen", on);
        }
        public double? GetTimePos()
        {
            return GetPropertyDouble("time-pos");
        }
        public double? GetDuration()
        {
            return GetPropertyDouble("duration");
        }
        double? GetPropertyDouble(string name)
        {
            var p = mpv_get_property_string(_handle, name);
            if (p == IntPtr.Zero) return null;
            try
            {
                var s = Marshal.PtrToStringUTF8(p);
                if (string.IsNullOrEmpty(s)) return null;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
                return null;
            }
            finally
            {
                mpv_free(p);
            }
        }
        public void Stop()
        {
            var args = new string[] { "stop", null! };
            mpv_command(_handle, args);
        }
        public void Command(params string[] args)
        {
            var ptrs = new IntPtr[args.Length + 1];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null)
                {
                    ptrs[i] = IntPtr.Zero;
                    continue;
                }
                var bytes = Encoding.UTF8.GetBytes(args[i]);
                var mem = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                Marshal.WriteByte(mem, bytes.Length, 0);
                ptrs[i] = mem;
            }
            ptrs[args.Length] = IntPtr.Zero;
            try
            {
                mpv_command_ptr(_handle, ptrs);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (ptrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(ptrs[i]);
                }
            }
        }
        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                mpv_terminate_destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
        enum mpv_format
        {
            MPV_FORMAT_NONE = 0,
            MPV_FORMAT_STRING = 1,
            MPV_FORMAT_OSD_STRING = 2,
            MPV_FORMAT_FLAG = 3,
            MPV_FORMAT_INT64 = 4,
            MPV_FORMAT_DOUBLE = 5,
            MPV_FORMAT_NODE = 6,
            MPV_FORMAT_NODE_ARRAY = 7,
            MPV_FORMAT_NODE_MAP = 8,
            MPV_FORMAT_BYTE_ARRAY = 9
        }
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr mpv_create();
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int mpv_initialize(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int mpv_set_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, mpv_format format, byte[] data);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int mpv_command(IntPtr ctx, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] args);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_command")]
        static extern int mpv_command_ptr(IntPtr ctx, IntPtr[] args);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void mpv_free(IntPtr data);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void mpv_terminate_destroy(IntPtr ctx);
    }
}
