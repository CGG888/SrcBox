using System;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services
{
    public static class MediaProbeService
    {
        public static async Task<TimeSpan?> ProbeDurationAsync(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath)) return null;
            var mpv = new LibmpvIptvClient.MpvInterop();
            try
            {
                mpv.Create();
                mpv.Initialize();
                var uri = new Uri(localPath, UriKind.Absolute).AbsoluteUri;
                mpv.LoadFile(uri);
                await Task.Delay(600);
                var len = mpv.GetDouble("duration");
                try { mpv.Pause(true); } catch { }
                if (len.HasValue && len.Value > 0) return TimeSpan.FromSeconds(len.Value);
            }
            catch { }
            finally
            {
                try { mpv.Dispose(); } catch { }
            }
            return null;
        }
    }
}
