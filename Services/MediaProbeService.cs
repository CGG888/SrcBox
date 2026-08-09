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
                // Poll for duration with smart polling instead of fixed 600ms delay
                var timeout = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < timeout)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                    var len = mpv.GetDouble("duration");
                    if (len.HasValue && len.Value > 0)
                    {
                        try { mpv.Pause(true); } catch { }
                        return TimeSpan.FromSeconds(len.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug("[MediaProbe] Probe failed: " + ex.Message);
            }
            finally
            {
                try { mpv.Dispose(); } catch { }
            }
            return null;
        }
    }
}
