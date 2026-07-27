using LibmpvIptvClient.Architecture.Application.Player;

namespace LibmpvIptvClient.Architecture.Platform.Player
{
    public class MpvPlayerEngineAdapter : IPlayerEngine
    {
        private readonly MpvInterop _mpv;

        public MpvPlayerEngineAdapter(MpvInterop mpv)
        {
            _mpv = mpv;
        }

        public void Play(string url)
        {
            _mpv.LoadFile(url);
        }

        public void Stop()
        {
            _mpv.Stop();
        }

        public void Pause(bool paused)
        {
            _mpv.Pause(paused);
        }

        public void SeekAbsolute(double seconds)
        {
            _mpv.SeekAbsolute(seconds);
        }

        public void SeekRelative(double seconds)
        {
            _mpv.SeekRelative(seconds);
        }

        public void SetVolume(double volume)
        {
            _mpv.SetVolume(volume);
        }

        public void SetMute(bool muted)
        {
            _mpv.Mute(muted);
        }

        public void SetSpeed(double speed)
        {
            _mpv.SetSpeed(speed);
        }

        public void SetAspectRatio(string ratio)
        {
            _mpv.SetAspectRatio(ratio);
        }

        /// <summary>
        /// 立即应用反交错设置到当前 mpv 实例。
        /// mode: "no" / "yes" / "auto"
        /// fieldParity: "auto" / "tff" / "bff"
        /// algorithm: "yadif" / "bwdif" / "none"
        /// 行为: 关闭时同时清空 vf，避免残留滤镜
        /// </summary>
        public void SetDeinterlace(string mode, string? fieldParity = null, string? algorithm = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mode)) mode = "no";
                _mpv.SetString("deinterlace", mode);

                // 场序（仅在 deinterlace != no 时才有意义）
                if (!string.Equals(mode, "no", StringComparison.OrdinalIgnoreCase))
                {
                    var parity = string.IsNullOrWhiteSpace(fieldParity) ? "auto" : fieldParity;
                    _mpv.SetString("deinterlace-field-parity", parity);
                }

                // 算法：关闭时清空 vf
                if (string.Equals(mode, "no", StringComparison.OrdinalIgnoreCase))
                {
                    // 清空 vf（mpv 支持 video-params/vo 直接置空）
                    _mpv.SetString("vf", "");
                }
                else
                {
                    var algo = string.IsNullOrWhiteSpace(algorithm) ? "yadif" : algorithm;
                    if (!string.Equals(algo, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        // 模式 1: 无延迟，但可能闪烁；模式 0: 一帧延迟，更稳定
                        var vfArg = $"{algo}=mode=1";
                        _mpv.SetString("vf", vfArg);
                    }
                    else
                    {
                        _mpv.SetString("vf", "");
                    }
                }
            }
            catch { /* 静默失败，不影响播放 */ }
        }

        public double? GetTimePos()
        {
            return _mpv.GetTimePos();
        }

        public double? GetDuration()
        {
            return _mpv.GetDuration();
        }

        public void EnsureReadyForLoad()
        {
            try
            {
                _mpv.Pause(false);
                var eof = _mpv.GetString("eof-reached");
                if (string.Equals(eof ?? "", "yes", System.StringComparison.OrdinalIgnoreCase))
                {
                    _mpv.Stop();
                    System.Threading.Thread.Sleep(80);
                }
            }
            catch { }
        }

        public bool IsEofReached()
        {
            try
            {
                var eof = _mpv.GetString("eof-reached");
                return string.Equals(eof ?? "", "yes", System.StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void LoadWithPrefetch(string url, System.Collections.Generic.IEnumerable<string> nextUrls)
        {
            _mpv.LoadWithPrefetch(url, nextUrls);
        }

        public void SetPropertyString(string name, string value)
        {
            _mpv.SetString(name, value);
        }

        public string? GetPropertyString(string name)
        {
            return _mpv.GetString(name);
        }

        public double? GetPropertyDouble(string name)
        {
            // MpvInterop has GetDouble? No, it has GetTimePos which calls GetPropertyDouble("time-pos")
            // Wait, looking at MpvInterop code from search result:
            // public double? GetTimePos() { return GetPropertyDouble("time-pos"); }
            // So MpvInterop DOES have GetPropertyDouble but maybe it's private or I missed it?
            // Re-checking search result for MpvInterop.cs...
            // It has: void SetDouble(string name, double value)
            // It has: public double? GetTimePos() { return GetPropertyDouble("time-pos"); }
            // So GetPropertyDouble MUST exist in MpvInterop.
            // Let's assume it is public or make it public if needed.
            // Actually, I'll just use GetString and parse it to be safe if I can't see the definition.
            // BUT, looking at line 278 of MpvInterop.cs in search result:
            // public double? GetTimePos() { return GetPropertyDouble("time-pos"); }
            // This implies GetPropertyDouble exists.
            // Let's try to call it. If it fails to compile, I will fix MpvInterop.
            // However, to be safe and avoid back-and-forth, I will use _mpv.GetDouble(name) if it exists, or parse string.
            // The search result showed:
            // var fps1 = _mpv.GetDouble("estimated-vf-fps") ?? _mpv.GetDouble("fps") ?? 0;
            // in MainWindow.xaml.cs. So _mpv.GetDouble IS public.
            return _mpv.GetDouble(name);
        }

        public long? GetPropertyLong(string name)
        {
            var s = _mpv.GetString(name);
            if (long.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }

        public bool? GetPropertyBool(string name)
        {
             // MpvInterop might not have GetBool.
             // I'll parse string.
             var s = _mpv.GetString(name);
             if (string.IsNullOrEmpty(s)) return null;
             if (string.Equals(s, "yes", System.StringComparison.OrdinalIgnoreCase) || s == "1" || string.Equals(s, "true", System.StringComparison.OrdinalIgnoreCase)) return true;
             if (string.Equals(s, "no", System.StringComparison.OrdinalIgnoreCase) || s == "0" || string.Equals(s, "false", System.StringComparison.OrdinalIgnoreCase)) return false;
             return null;
        }
    }
}