using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services
{
    public static class HttpTsRecorder
    {
        public static async Task StartAsync(HttpClient http, string url, string filePath, CancellationToken token)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                if (!resp.IsSuccessStatusCode) return;
                var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                try { Logger.Debug($"[RecordHTTP] start url={url} type={ct} -> {filePath}"); } catch { }
                using var src = await resp.Content.ReadAsStreamAsync(token);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using var dst = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 64, useAsync: true);
                var buf = new byte[1024 * 64];
                while (!token.IsCancellationRequested)
                {
                    var n = await src.ReadAsync(buf, 0, buf.Length, token);
                    if (n <= 0) break;
                    await dst.WriteAsync(buf.AsMemory(0, n), token);
                }
                await dst.FlushAsync(token);
                try { Logger.Debug($"[RecordHTTP] done size={new FileInfo(filePath).Length}"); } catch { }
            }
            catch (OperationCanceledException)
            {
                try { Logger.Debug("[RecordHTTP] canceled"); } catch { }
            }
            catch (Exception ex)
            {
                try { Logger.Error("[RecordHTTP] error " + ex.Message); } catch { }
            }
        }
        public static bool IsHttpTsLike(string url, string? contentTypeHint = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var u = url.ToLowerInvariant();
            if (u.StartsWith("http://") || u.StartsWith("https://"))
            {
                if (u.EndsWith(".m3u8")) return false;
                if (u.Contains("/rtp/")) return true;
                if (u.EndsWith(".ts")) return true;
                if (!string.IsNullOrEmpty(contentTypeHint))
                {
                    var ct = contentTypeHint.ToLowerInvariant();
                    if (ct.Contains("video/mp2t") || ct.Contains("application/octet-stream")) return true;
                    if (ct.Contains("application/vnd.apple.mpegurl")) return false;
                }
                return true; // 默认尽量尝试
            }
            return false;
        }
    }
}
