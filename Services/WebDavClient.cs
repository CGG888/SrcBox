using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Diagnostics;
using LibmpvIptvClient.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Net;

namespace LibmpvIptvClient.Services
{
    public class WebDavClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly bool _allowSelfSigned;
        private readonly string _authHint = "auth=no";
        static string Classify(int code)
        {
            if (code >= 200 && code <= 299) return "ok";
            if (code == 401) return "unauthorized";
            if (code == 403) return "forbidden";
            if (code == 404) return "not_found";
            if (code == 405) return "method_not_allowed";
            if (code == 409) return "conflict";
            if (code >= 400 && code <= 499) return "client_error";
            if (code >= 500 && code <= 599) return "server_error";
            return "unknown";
        }
        public WebDavClient(LibmpvIptvClient.WebDavConfig cfg)
        {
            var handler = new HttpClientHandler();
            if (cfg.AllowSelfSignedCert)
            {
                handler.ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true;
            }
            _http = new HttpClient(handler);
            _baseUrl = (cfg.BaseUrl ?? "").Trim();
            _allowSelfSigned = cfg.AllowSelfSignedCert;
            // 兼容：若未显式提供明文 Token，则使用加密存储的 Token 解密回填
            var token = cfg.TokenOrPassword;
            try
            {
                if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(cfg.EncryptedToken))
                {
                    token = LibmpvIptvClient.Services.CryptoUtil.UnprotectString(cfg.EncryptedToken);
                }
            }
            catch { }
            var hasAuth = !string.IsNullOrWhiteSpace(cfg.Username) || !string.IsNullOrWhiteSpace(token);
            if (hasAuth)
            {
                var raw = $"{cfg.Username}:{token}";
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", b64);
            }
            _authHint = $"auth={(hasAuth ? "yes" : "no")} user={(string.IsNullOrWhiteSpace(cfg.Username) ? "empty" : "set")} tok={(string.IsNullOrWhiteSpace(token) ? "empty" : "set")}";
        }
        public string Combine(string path)
        {
            var b = (_baseUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(b)) return ("/" + (path ?? "").TrimStart('/'));
            if (!b.EndsWith("/")) b += "/";
            var p = (path ?? "").TrimStart('/');
            try { p = p.Replace("//", "/"); } catch { }
            try
            {
                var baseUri = new Uri(b, UriKind.Absolute);
                var u = new Uri(baseUri, p);
                return u.ToString();
            }
            catch
            {
                return b + p;
            }
        }
        public async Task<bool> TestConnectionAsync(string baseUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) return false;
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), baseUrl);
                req.Headers.Add("Depth", "0");
                req.Content = new StringContent("");
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] TEST {SanUrl(baseUrl)} depth=0 status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms selfSigned={_allowSelfSigned} {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] TEST {SanUrl(baseUrl)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<bool> MkcolAsync(string url)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] MKCOL {SanUrl(url)} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode || (int)resp.StatusCode == 405 || (int)resp.StatusCode == 409;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] MKCOL {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<bool> EnsureCollectionAsync(string path)
        {
            try
            {
                var segs = (path ?? "/").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var cur = "";
                foreach (var s in segs)
                {
                    cur += "/" + s;
                    var ok = await MkcolAsync(Combine(cur));
                    if (!ok)
                    {
                        // MKCOL 405/409 表示已存在，继续；其他失败则中止
                        // 已在 MkcolAsync 内处理 405/409 视为成功，这里仅继续
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        public async Task<bool> PutAsync(string url, byte[] data, string contentType = "application/octet-stream")
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(HttpMethod.Put, url);
                req.Content = new ByteArrayContent(data);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] PUT {SanUrl(url)} size={data?.Length ?? 0} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] PUT {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<bool> PutFileAsync(string url, string filePath, string contentType = "application/octet-stream", int maxKBps = 0, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists) return false;
                var t0 = Stopwatch.StartNew();
                using (var req = new HttpRequestMessage(HttpMethod.Put, url))
                {
                    HttpContent content;
                    if (maxKBps > 0)
                    {
                        content = new ThrottledFileContent(filePath, maxKBps);
                    }
                    else
                    {
                        content = new StreamContent(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                    }
                    using (content)
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                        req.Content = content;
                        using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            t0.Stop();
                            try
                            {
                                var code = (int)resp.StatusCode;
                                var cls = Classify(code);
                                Logger.Debug($"[WebDAV] PUT {SanUrl(url)} size={fi.Length} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms kbps={(maxKBps>0?maxKBps:0)} {_authHint}");
                            }
                            catch { }
                            return resp.IsSuccessStatusCode;
                        }
                    }
                }
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] PUT {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        class ThrottledFileContent : HttpContent
        {
            private readonly string _path;
            private readonly int _maxKBps;
            public ThrottledFileContent(string path, int maxKBps)
            {
                _path = path;
                _maxKBps = Math.Max(1, maxKBps);
            }
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                const int chunk = 64 * 1024;
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buf = new byte[chunk];
                long sent = 0;
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    int read = await fs.ReadAsync(buf, 0, buf.Length);
                    if (read <= 0) break;
                    await stream.WriteAsync(buf, 0, read);
                    sent += read;
                    if (_maxKBps > 0)
                    {
                        double expectedMs = (sent / 1024.0) / _maxKBps * 1000.0;
                        var delta = expectedMs - sw.Elapsed.TotalMilliseconds;
                        if (delta > 1) await Task.Delay((int)delta);
                    }
                }
            }
            protected override bool TryComputeLength(out long length)
            {
                try { length = new FileInfo(_path).Length; return true; } catch { length = 0; return false; }
            }
        }
        public async Task<bool> DeleteAsync(string url)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(HttpMethod.Delete, url);
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] DELETE {SanUrl(url)} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode || (int)resp.StatusCode == 404;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] DELETE {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<bool> MoveAsync(string srcUrl, string dstUrl, bool overwrite = true)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("MOVE"), srcUrl);
                // Destination 必须为绝对 URI，且部分实现需要百分号编码完整路径
                var dest = dstUrl;
                try { dest = new Uri(dstUrl).ToString(); } catch { }
                req.Headers.TryAddWithoutValidation("Destination", dest);
                if (overwrite) req.Headers.TryAddWithoutValidation("Overwrite", "T");
                req.Headers.TryAddWithoutValidation("Depth", "infinity");
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] MOVE {SanUrl(srcUrl)} -> {SanUrl(dstUrl)} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] MOVE {SanUrl(srcUrl)} -> {SanUrl(dstUrl)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<bool> CopyAsync(string srcUrl, string dstUrl, bool overwrite = true)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("COPY"), srcUrl);
                var dest = dstUrl;
                try { dest = new Uri(dstUrl).ToString(); } catch { }
                req.Headers.TryAddWithoutValidation("Destination", dest);
                if (overwrite) req.Headers.TryAddWithoutValidation("Overwrite", "T");
                req.Headers.TryAddWithoutValidation("Depth", "infinity");
                var resp = await _http.SendAsync(req);
                t0.Stop();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] COPY {SanUrl(srcUrl)} -> {SanUrl(dstUrl)} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] COPY {SanUrl(srcUrl)} -> {SanUrl(dstUrl)} class=exception {_authHint}"); } catch { }
                return false;
            }
        }
        public async Task<(bool move, bool copy)> ProbeCapabilitiesAsync(string baseUrl)
        {
            var move = false; var copy = false;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Options, baseUrl);
                var resp = await _http.SendAsync(req);
                if (resp != null)
                {
                    if (resp.Headers.TryGetValues("Allow", out var vals))
                    {
                        var s = string.Join(",", vals).ToUpperInvariant();
                        move = s.Contains("MOVE");
                        copy = s.Contains("COPY");
                    }
                }
            }
            catch { }
            return (move, copy);
        }
        public async Task<(bool ok, byte[] bytes)> GetBytesAsync(string url, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var resp = await _http.GetAsync(url, ct);
                t0.Stop();
                var ok = resp.IsSuccessStatusCode;
                byte[] buf = Array.Empty<byte>();
                if (ok) buf = await resp.Content.ReadAsByteArrayAsync(ct);
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] GET {SanUrl(url)} status={code} class={cls} reason={resp.ReasonPhrase} size={buf.Length} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return (ok, buf);
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] GET {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return (false, Array.Empty<byte>());
            }
        }
        
        public async Task<(bool ok, long size, DateTime? lastmod)> HeadAsync(string url, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(HttpMethod.Head, url);
                var resp = await _http.SendAsync(req, ct);
                t0.Stop();
                long size = 0;
                DateTime? lastmod = null;
                if (resp.Content.Headers.ContentLength.HasValue) size = resp.Content.Headers.ContentLength.Value;
                if (resp.Content.Headers.LastModified.HasValue) lastmod = resp.Content.Headers.LastModified.Value.UtcDateTime;
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] HEAD {SanUrl(url)} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                return (resp.IsSuccessStatusCode, size, lastmod);
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] HEAD {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return (false, 0, null);
            }
        }
        static ConcurrentDictionary<string, (long size, DateTime? lastmod)> _headCache = new ConcurrentDictionary<string, (long, DateTime?)>();
        static bool _cacheLoaded = false;
        static DateTime _cacheLastSave = DateTime.MinValue;
        static string CacheFilePath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, "webdav_cache.json");
            }
            catch
            {
                return "webdav_cache.json";
            }
        }
        static void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;
            try
            {
                var f = CacheFilePath();
                if (File.Exists(f))
                {
                    var json = File.ReadAllText(f);
                    var arr = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, CacheItem>>(json);
                    if (arr != null)
                    {
                        foreach (var kv in arr)
                        {
                            _headCache[kv.Key] = (kv.Value.size, kv.Value.lastmod);
                        }
                    }
                }
            }
            catch { }
        }
        static void SaveCacheMaybe()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _cacheLastSave).TotalSeconds < 30) return;
                _cacheLastSave = now;
                var dict = new System.Collections.Generic.Dictionary<string, CacheItem>();
                foreach (var kv in _headCache)
                {
                    dict[kv.Key] = new CacheItem { size = kv.Value.size, lastmod = kv.Value.lastmod };
                }
                var json = JsonSerializer.Serialize(dict);
                var f = CacheFilePath();
                File.WriteAllText(f, json);
            }
            catch { }
        }
        class CacheItem
        {
            public long size { get; set; }
            public DateTime? lastmod { get; set; }
        }
        public async Task<(bool ok, long size, DateTime? lastmod)> HeadAsync(string url)
        {
            try
            {
                EnsureCacheLoaded();
                if (_headCache.TryGetValue(url, out var c)) return (true, c.size, c.lastmod);
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(HttpMethod.Head, url);
                var resp = await _http.SendAsync(req);
                t0.Stop();
                var ok = resp.IsSuccessStatusCode;
                long size = 0;
                DateTime? lm = null;
                if (resp.Content?.Headers?.ContentLength.HasValue == true) size = resp.Content.Headers.ContentLength.Value;
                if (resp.Content?.Headers?.LastModified.HasValue == true) lm = resp.Content.Headers.LastModified.Value.UtcDateTime;
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] HEAD {SanUrl(url)} status={code} class={cls} size={size} lastmod={lm} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                if (ok)
                {
                    _headCache[url] = (size, lm);
                    SaveCacheMaybe();
                }
                return (ok, size, lm);
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] HEAD {SanUrl(url)} class=exception {_authHint}"); } catch { }
                return (false, 0, null);
            }
        }
        public async Task<List<string>> ListAsync(string url, int depth = 1)
        {
            var res = new List<string>();
            try
            {
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                req.Headers.Add("Depth", depth.ToString());
                // 发送标准 WebDAV PROPFIND XML，兼容更多服务端
                const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>";
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/xml");
                var resp = await _http.SendAsync(req);
                t0.Stop();
                var ok = resp.IsSuccessStatusCode;
                var xml = await resp.Content.ReadAsStringAsync();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] LIST {SanUrl(url)} depth={depth} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                if (!ok) return res;
                var matches = System.Text.RegularExpressions.Regex.Matches(xml, "<d:href>(.*?)</d:href>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                    if (string.IsNullOrEmpty(href)) continue;
                    if (href.EndsWith("/")) continue;
                    res.Add(href);
                }
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] LIST {SanUrl(url)} class=exception {_authHint}"); } catch { }
            }
            return res;
        }
        public async Task<System.Collections.Generic.List<(string href, long size, DateTime? lastmod)>> ListWithPropsAsync(string url, int depth = 1)
        {
            var res = new System.Collections.Generic.List<(string href, long size, DateTime? lastmod)>();
            try
            {
                EnsureCacheLoaded();
                var t0 = Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                req.Headers.Add("Depth", depth.ToString());
                const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>";
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/xml");
                var resp = await _http.SendAsync(req);
                t0.Stop();
                var ok = resp.IsSuccessStatusCode;
                var xml = await resp.Content.ReadAsStringAsync();
                try
                {
                    var code = (int)resp.StatusCode;
                    var cls = Classify(code);
                    Logger.Debug($"[WebDAV] LIST+ {SanUrl(url)} depth={depth} status={code} class={cls} reason={resp.ReasonPhrase} elapsed={t0.ElapsedMilliseconds}ms {_authHint}");
                }
                catch { }
                if (!ok) return res;
                var responses = System.Text.RegularExpressions.Regex.Split(xml, "</?d:response[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var block in responses)
                {
                    if (string.IsNullOrWhiteSpace(block)) continue;
                    var hrefM = System.Text.RegularExpressions.Regex.Match(block, "<d:href>(.*?)</d:href>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!hrefM.Success) continue;
                    var href = System.Net.WebUtility.HtmlDecode(hrefM.Groups[1].Value ?? "");
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    if (href.EndsWith("/")) continue;
                    long size = 0;
                    DateTime? lm = null;
                    // size
                    var sizeM = System.Text.RegularExpressions.Regex.Match(block, "<d:getcontentlength>(.*?)</d:getcontentlength>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (sizeM.Success)
                    {
                        if (long.TryParse(sizeM.Groups[1].Value.Trim(), out var s) && s >= 0) size = s;
                    }
                    // last modified
                    var lmM = System.Text.RegularExpressions.Regex.Match(block, "<d:getlastmodified>(.*?)</d:getlastmodified>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (lmM.Success)
                    {
                        var s = System.Net.WebUtility.HtmlDecode(lmM.Groups[1].Value).Trim();
                        try { lm = DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime(); } catch { }
                    }
                    res.Add((href, size, lm));
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(href) && (size > 0 || lm.HasValue))
                        {
                            var abs = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Combine(href);
                            _headCache[abs] = (size, lm);
                            SaveCacheMaybe();
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                try { Logger.Debug($"[WebDAV] LIST+ {SanUrl(url)} class=exception {_authHint}"); } catch { }
            }
            return res;
        }
        static string SanHost(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var u)) return u.Host;
            }
            catch { }
            return "(invalid)";
        }
        static string SanUrl(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                {
                    var port = u.IsDefaultPort ? "" : ":" + u.Port.ToString();
                    var maskedHost = "***";
                    return $"{u.Scheme}://{maskedHost}{port}{u.AbsolutePath}";
                }
            }
            catch { }
            return url;
        }
    }
}
