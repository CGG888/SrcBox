using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Services.WebRemote.Voice;

namespace LibmpvIptvClient.Services.WebRemote
{
    public class WebRemoteServer : IDisposable
    {
        private TcpListener? _tcpListener;
        private X509Certificate2? _tlsCertificate;
        private readonly object _certificateLock = new();
        private CancellationTokenSource? _cts;
        private readonly List<WebSocket> _clients = new();
        private readonly object _clientsLock = new();

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        /// <summary>
        /// True when a server certificate is loaded, so TLS clients are accepted on the same port as
        /// plain http clients.
        /// </summary>
        public bool IsTlsAvailable { get; private set; }

        /// <summary>
        /// First byte of a TLS record (a handshake). Plain http always starts with an ASCII verb, so a
        /// single byte is enough to tell the two protocols apart on one shared port.
        /// </summary>
        internal const byte TlsHandshakePrefix = 0x16;

        internal static bool LooksLikeTlsHandshake(byte firstByte) => firstByte == TlsHandshakePrefix;

        // 密码保护
        private bool _requirePassword;
        private string _password = "";
        // NEW-13: ConcurrentDictionary instead of HashSet + lock, eliminating data race.
        // Keyed by the connection itself: the old string key came from ws.SubProtocol, which is always
        // empty here, so one authenticated device silently unlocked every other client on the LAN.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<WebSocket, bool> _authenticatedClients = new();

        public Func<WebRemoteStatus>? GetStatusCallback { get; set; }
        public Func<List<WebRemoteChannelGroup>>? GetChannelsCallback { get; set; }
        public Func<string, DateTime?, List<WebRemoteProgram>>? GetEpgCallback { get; set; }
        public Action? PlayCallback { get; set; }
        public Action? PauseCallback { get; set; }
        public Action? StopCallback { get; set; }
        public Action<double>? SetVolumeCallback { get; set; }
        public Action<string>? ChangeChannelCallback { get; set; }
        public Action? ExitCallback { get; set; }
        public Action? FullscreenCallback { get; set; }
        public Action? SwitchSourceCallback { get; set; }
        public Func<List<WebRemoteSource>>? GetSourcesCallback { get; set; }
        public Action<string>? SelectSourceCallback { get; set; }
        public Action<string, string>? AddSourceCallback { get; set; }
        public Action<string>? RemoveSourceCallback { get; set; }
        public Action<double>? SeekCallback { get; set; }
        public Action<double>? SetSpeedCallback { get; set; }
        public Action<bool>? SetTimeshiftCallback { get; set; }
        public Action<double>? SeekTimeshiftCallback { get; set; }
        public Func<WebRemoteTimeshift?>? GetTimeshiftCallback { get; set; }
        public Action<string, string, string, string>? ReplayProgramCallback { get; set; }
        public Func<List<WebRemoteReminder>>? GetRemindersCallback { get; set; }
        public Func<string, string, string, string, string, int, int?, string>? AddReminderCallback { get; set; }
        public Action<string>? CancelReminderCallback { get; set; }
        public Func<List<WebRemoteRecording>>? GetRecordingsCallback { get; set; }
        public Action<string>? StopRecordingCallback { get; set; }
        public Action<string>? DeleteRecordingCallback { get; set; }

        /// <summary>
        /// Voice pipeline entry point. The router itself lives in VoiceCommandRouter and is shared by the
        /// websocket action and the /api/voice endpoint, so both paths understand exactly the same
        /// sentences and the same user rules. A non-empty action runs that action directly.
        /// </summary>
        public Func<VoiceCommandContext, string, string, VoiceCommandReply>? VoiceCommandCallback { get; set; }

        /// <summary>Shared secret for /api/voice; empty disables the endpoint's token check (LAN only).</summary>
        private string _voiceToken = "";

        /// <summary>The token a phone-side shortcut has to pass as ?token=; empty when unset.</summary>
        public string VoiceToken => _voiceToken;

        public void SetVoiceToken(string? token) => _voiceToken = token ?? "";

        /// <summary>
        /// Starts the remote on a single port that speaks both plain http and https: the first byte of
        /// every connection decides which protocol the client uses. Android Chrome needs the https side
        /// before it treats the page as a secure context and offers to install it as an app, while the
        /// plain http side stays reachable on the very same address for the first visit.
        /// </summary>
        public void Start(int port, bool requirePassword = false, string password = "")
        {
            if (IsRunning) return;

            Port = port;
            _requirePassword = requirePassword;
            _password = password ?? "";
            _authenticatedClients.Clear();
            _cts = new CancellationTokenSource();

            if (EnsureTlsCertificate() == null)
            {
                Logger.Warn("[WebRemote] No certificate available, the remote will answer plain http only");
            }

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                IsRunning = true;
                _ = Task.Run(() => AcceptClientsAsync(_tcpListener, _cts.Token));
                Logger.Debug($"[WebRemote] Server started on port {port}{(IsTlsAvailable ? " (http + https)" : " (http only)")}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to start server: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            if (!IsRunning && !IsTlsAvailable) return;

            _cts?.Cancel();
            lock (_clientsLock)
            {
                foreach (var ws in _clients.ToList())
                {
                    try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(); } catch { }
                }
                _clients.Clear();
            }
            _tcpListener?.Stop();
            _tcpListener = null;
            _tlsCertificate = null;
            IsRunning = false;
            IsTlsAvailable = false;
            Logger.Debug("[WebRemote] Server stopped");
        }

        /// <summary>
        /// The https certificate, resolved at Start and re-resolved on demand: a profile whose key store
        /// was unavailable when the app started recovers on the next https visit instead of staying http
        /// only until the app is restarted.
        /// </summary>
        private X509Certificate2? EnsureTlsCertificate()
        {
            var cached = _tlsCertificate;
            if (cached != null) return cached;

            lock (_certificateLock)
            {
                if (_tlsCertificate != null) return _tlsCertificate;

                if (!WebRemoteCertificateService.TryGetServerCertificate(out var certificate)) return null;

                _tlsCertificate = certificate;
                IsTlsAvailable = true;
                Logger.Info("[WebRemote] Certificate obtained for an incoming https client");
                return certificate;
            }
        }

        private async Task AcceptClientsAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Logger.Error($"[WebRemote] Accept error: {ex.Message}"); }
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
        {
            using (tcpClient)
            {
                try
                {
                    var networkStream = tcpClient.GetStream();
                    networkStream.ReadTimeout = 5000;
                    tcpClient.ReceiveTimeout = 5000;

                    // Port unification: look at the first byte without consuming it, since a TLS client
                    // needs that byte to stay in the stream for the handshake.
                    var head = new byte[1];
                    bool useTls;
                    try
                    {
                        useTls = tcpClient.Client.Receive(head, 0, 1, SocketFlags.Peek) == 1
                                 && LooksLikeTlsHandshake(head[0]);
                    }
                    catch (SocketException)
                    {
                        return;
                    }

                    Logger.Debug($"[WebRemote] Client connected from {tcpClient.Client.RemoteEndPoint}{(useTls ? " (https)" : "")}");

                    Stream stream = networkStream;
                    if (useTls)
                    {
                        var certificate = EnsureTlsCertificate();
                        if (certificate == null)
                        {
                            Logger.Warn("[WebRemote] TLS client rejected: no certificate available");
                            return;
                        }

                        var ssl = new SslStream(networkStream, false);
                        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = certificate,
                            ClientCertificateRequired = false,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        }, ct);
                        stream = ssl;
                    }

                    // Read all HTTP headers first
                    var sb = new StringBuilder();
                    var buf = new byte[8192];
                    int read;

                    while ((read = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buf, 0, read));
                        if (sb.ToString().Contains("\r\n\r\n")) break;
                    }

                    var request = sb.ToString();
                    Logger.Debug($"[WebRemote] Request length: {request.Length}, starts with: {request.Substring(0, Math.Min(20, request.Length)).Replace("\r\n", "\\r\\n")}");

                    // Check if it's a WebSocket upgrade request
                    bool isWebSocket = request.Contains("Upgrade:") && request.Contains("websocket");
                    Logger.Debug($"[WebRemote] Is WebSocket upgrade: {isWebSocket}");

                    if (isWebSocket)
                    {
                        await HandleWebSocketAsync(tcpClient, stream, request, ct);
                    }
                    else
                    {
                        await ServeHttpPageAsync(tcpClient, stream, request, ct);
                    }
                }
                catch (Exception ex) { Logger.Error($"[WebRemote] HandleClient error: {ex.Message}"); }
            }
        }

        private async Task ServeHttpPageAsync(TcpClient tcpClient, Stream stream, string request, CancellationToken ct)
        {
            try
            {
                // Check if it's a logo request
                var lines = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var firstLine = lines.Length > 0 ? lines[0] : "";
                if (firstLine.StartsWith("GET /logo/"))
                {
                    await ServeLogoAsync(tcpClient, stream, firstLine, ct);
                    return;
                }

                // PWA: CA certificate download, used before the phone can open the https address.
                // Served in DER, PEM and Apple profile flavour because the three phone installers want
                // different files.
                var requestPath = RequestPath(firstLine);
                if (requestPath is "/cert" or "/cert.crt" or "/cert.der")
                {
                    await ServeCertificateAsync(tcpClient, stream, ct);
                    return;
                }

                if (requestPath == "/cert.pem")
                {
                    await ServeCertificatePemAsync(tcpClient, stream, ct);
                    return;
                }

                if (requestPath is "/cert.mobileconfig" or "/profile.mobileconfig")
                {
                    await ServeCertificateProfileAsync(tcpClient, stream, ct);
                    return;
                }

                // PWA: manifest.json
                if (firstLine.TrimStart().StartsWith("GET /manifest.json"))
                {
                    await ServeManifestAsync(tcpClient, stream, ct);
                    return;
                }

                // PWA: service worker
                if (firstLine.TrimStart().StartsWith("GET /sw.js"))
                {
                    await ServeServiceWorkerAsync(tcpClient, stream, ct);
                    return;
                }

                // PWA: favicon.ico
                if (firstLine.TrimStart().StartsWith("GET /favicon.ico"))
                {
                    await ServeFaviconAsync(tcpClient, stream, ct);
                    return;
                }

                // PWA: app icons
                if (firstLine.TrimStart().StartsWith("GET /icons/"))
                {
                    await ServeIconAsync(tcpClient, stream, firstLine, ct);
                    return;
                }

                // Voice endpoint: a phone-side assistant (ColorOS shortcut, Tasker, an Apple shortcut)
                // can drive the player without opening the page at all. Read-only GET, LAN clients only.
                if (requestPath == "/api/voice")
                {
                    await ServeVoiceApiAsync(tcpClient, stream, firstLine, ct);
                    return;
                }

                Logger.Debug("[WebRemote] Sending HTML page");
                var lang = ParseAcceptLanguage(request);
                var html = GetRemoteHtml(lang, IsTlsAvailable ? Port : 0);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n";
                var headerBuf = Encoding.UTF8.GetBytes(header);
                var bodyBuf = Encoding.UTF8.GetBytes(html);
                await stream.WriteAsync(headerBuf, ct);
                await stream.WriteAsync(bodyBuf, ct);
                Logger.Debug("[WebRemote] HTML page sent");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] ServeHttpPage error: {ex.Message}");
            }
            finally { tcpClient.Close(); }
        }

        /// <summary>
        /// True when a /logo/ request may be served: the target must be an image inside the logo cache
        /// directory or the application directory. Without this the endpoint handed out any readable file
        /// on the machine to anyone on the LAN.
        /// </summary>
        public static bool IsAllowedLogoPath(string? filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                var full = Path.GetFullPath(filePath);
                var ext = Path.GetExtension(full).ToLowerInvariant();
                if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico")) return false;

                foreach (var root in AllowedLogoRoots())
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        static string[] AllowedLogoRoots()
        {
            string cache = "", baseDir = "", exeDir = "";
            try { cache = LibmpvIptvClient.Services.LogoCacheService.Instance.CacheDirectory; } catch { }
            try { baseDir = AppContext.BaseDirectory; } catch { }
            try { exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? ""; } catch { }
            return new[] { cache, baseDir, exeDir };
        }

        private async Task ServeLogoAsync(TcpClient? tcpClient, Stream stream, string request, CancellationToken ct)
        {
            try
            {
                // Parse: GET /logo/<encoded_path>
                var parts = request.Split(' ');
                if (parts.Length < 2)
                {
                    await SendErrorAsync(stream, "400 Bad Request", ct);
                    return;
                }
                var encodedPath = parts[1].Substring("/logo/".Length);
                var filePath = Uri.UnescapeDataString(encodedPath);

                Logger.Debug($"[WebRemote] Logo request: {filePath}");

                // The endpoint used to serve whatever path the client asked for, so anyone on the LAN could
                // read arbitrary files (settings, credentials, ...). Only images from the logo cache or the
                // application directory are served now.
                if (!IsAllowedLogoPath(filePath))
                {
                    Logger.Warn($"[WebRemote] 拒绝非台标缓存目录的 /logo/ 请求: {filePath}");
                    await SendErrorAsync(stream, "403 Forbidden", ct);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    Logger.Warn($"[WebRemote] Logo file not found: {filePath}");
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };

                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {fileBytes.Length}\r\nCache-Control: max-age=86400\r\nConnection: close\r\n\r\n";
                var headerBuf = Encoding.UTF8.GetBytes(header);
                await stream.WriteAsync(headerBuf, ct);
                await stream.WriteAsync(fileBytes, ct);
                Logger.Debug($"[WebRemote] Logo served: {filePath} ({fileBytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] ServeLogo error: {ex.Message}");
            }
            finally { if (tcpClient != null) tcpClient.Close(); }
        }

        private async Task SendErrorAsync(Stream stream, string message, CancellationToken ct)
        {
            var body = $"<html><body><h1>{message}</h1></body></html>";
            var header = $"HTTP/1.1 {message}\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n";
            var headerBuf = Encoding.UTF8.GetBytes(header);
            var bodyBuf = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(headerBuf, ct);
            await stream.WriteAsync(bodyBuf, ct);
        }

        /// <summary>
        /// LAN-only check for /api/voice. The endpoint has no websocket handshake to carry the session, so
        /// it relies on the token plus this address filter: a peer that is not on a private/loopback range
        /// is refused outright.
        /// </summary>
        internal static bool IsPrivateAddress(string? remoteAddress)
            => !IPAddress.TryParse(remoteAddress, out var ip) || IPAddress.IsLoopback(ip) || IsPrivateRange(ip);

        private static bool IsPrivateRange(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // APIPA and CGNAT join the three RFC1918 blocks the address helper already knows.
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
                return WebRemoteAddressHelper.IsPrivateRange(ip.ToString());
            }

            // IPv6: unique local (fc00::/7) and link local (fe80::/10) are the LAN equivalents.
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
                var b = ip.GetAddressBytes();
                return (b[0] & 0xFE) == 0xFC;
            }

            return false;
        }

        /// <summary>
        /// GET /api/voice?text=换到1台&amp;token=... runs one spoken sentence, and
        /// GET /api/voice?action=channel&amp;target=1 does the same without natural language, which is how a
        /// phone assistant maps a fixed phrase onto a fixed channel. ?dry=1 returns the candidates instead
        /// of acting, so a shortcut can show a menu.
        /// </summary>
        private async Task ServeVoiceApiAsync(TcpClient tcpClient, Stream stream, string requestLine, CancellationToken ct)
        {
            try
            {
                var remote = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                if (!IsPrivateAddress(remote?.Address.ToString()))
                {
                    Logger.Warn("[WebRemote] /api/voice refused: non private address");
                    await WriteVoiceJsonAsync(stream, 403, "Forbidden", new { success = false, error = "forbidden" }, ct);
                    return;
                }

                var query = QueryValues(requestLine);
                var token = query.TryGetValue("token", out var tokenValue) ? tokenValue : "";
                var requireToken = !string.IsNullOrEmpty(_voiceToken);
                if (requireToken && !string.Equals(token, _voiceToken, StringComparison.Ordinal))
                {
                    Logger.Warn("[WebRemote] /api/voice rejected: token mismatch");
                    await WriteVoiceJsonAsync(stream, 401, "Unauthorized", new { success = false, error = "unauthorized" }, ct);
                    return;
                }

                if (VoiceCommandCallback == null)
                {
                    await WriteVoiceJsonAsync(stream, 503, "Service Unavailable", new { success = false, error = "not_ready" }, ct);
                    return;
                }

                var text = query.TryGetValue("text", out var textValue) ? textValue : "";
                var action = query.TryGetValue("action", out var actionValue) ? actionValue : "";
                var target = query.TryGetValue("target", out var targetValue) ? targetValue : "";
                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(action))
                {
                    await WriteVoiceJsonAsync(stream, 400, "Bad Request", new { success = false, error = "missing_text" }, ct);
                    return;
                }

                var dry = query.TryGetValue("dry", out var dryValue)
                          && (dryValue == "1" || dryValue.Equals("true", StringComparison.OrdinalIgnoreCase));
                var context = new VoiceCommandContext
                {
                    Text = string.IsNullOrWhiteSpace(text) ? target : text,
                    Lang = query.TryGetValue("lang", out var langValue) && !string.IsNullOrWhiteSpace(langValue)
                        ? langValue
                        : ParseAcceptLanguage(query.TryGetValue("accept", out var acceptValue) ? acceptValue : ""),
                    DryRun = dry
                };

                var reply = VoiceCommandCallback(context, action, target);
                await WriteVoiceJsonAsync(stream, 200, "OK", reply, ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] /api/voice error: {ex.Message}");
            }
            finally { tcpClient.Close(); }
        }

        private static async Task WriteVoiceJsonAsync(Stream stream, int status, string reason, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
        }

        /// <summary>Parses the query string of a request line into decoded key/value pairs.</summary>
        internal static Dictionary<string, string> QueryValues(string requestLine)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var line = requestLine ?? "";
            var mark = line.IndexOf('?');
            if (mark < 0) return result;
            var query = line[(mark + 1)..];
            var space = query.IndexOf(' ');
            if (space >= 0) query = query[..space];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
                var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                if (key.Length > 0) result[key] = value;
            }
            return result;
        }

        private async Task ServeManifestAsync(TcpClient? tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var manifest = @"{
  ""id"": ""/"",
  ""name"": ""SrcBox Remote"",
  ""short_name"": ""SrcBox"",
  ""description"": ""SrcBox IPTV Remote Control"",
  ""start_url"": ""/"",
  ""scope"": ""/"",
  ""display"": ""standalone"",
  ""display_override"": [""standalone"", ""minimal-ui""],
  ""background_color"": ""#d9dee5"",
  ""theme_color"": ""#d9dee5"",
  ""orientation"": ""portrait"",
  ""icons"": [
    { ""src"": ""/icons/icon-192.png"", ""sizes"": ""192x192"", ""type"": ""image/png"", ""purpose"": ""any"" },
    { ""src"": ""/icons/icon-512.png"", ""sizes"": ""512x512"", ""type"": ""image/png"", ""purpose"": ""any"" },
    { ""src"": ""/icons/maskable-512.png"", ""sizes"": ""512x512"", ""type"": ""image/png"", ""purpose"": ""maskable"" }
  ],
  ""categories"": [""entertainment"", ""utilities""],
  ""lang"": ""en"",
  ""prefer_related_applications"": false
}";
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/manifest+json; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(manifest)}\r\nCache-Control: max-age=86400\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(manifest), ct);
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeManifest error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        private async Task ServeServiceWorkerAsync(TcpClient? tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var sw = @"
// SrcBox Web Remote Service Worker
var CACHE_NAME = 'srcbox-remote-v8';
var STATIC_ASSETS = [
  '/',
  '/manifest.json'
];

self.addEventListener('install', function(event) {
  event.waitUntil(
    caches.open(CACHE_NAME).then(function(cache) {
      return cache.addAll(STATIC_ASSETS);
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', function(event) {
  event.waitUntil(
    caches.keys().then(function(cacheNames) {
      return Promise.all(
        cacheNames.filter(function(name) {
          return name !== CACHE_NAME;
        }).map(function(name) {
          return caches.delete(name);
        })
      );
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', function(event) {
  // Skip non-GET requests
  if (event.request.method !== 'GET') return;

  // Skip WebSocket and logo requests
  var url = event.request.url;
  if (url.includes('/ws') || url.includes('/logo/') || url.includes('/icons/')) return;

  // The page itself is served network-first: a rebuilt remote UI has to reach the phone on the
  // next refresh, and the cached copy is only the offline fallback. Everything else stays
  // cache-first so a PWA launch keeps working without the app running.
  if (event.request.mode === 'navigate') {
    event.respondWith(
      fetch(event.request).then(function(networkResponse) {
        if (networkResponse && networkResponse.status === 200) {
          var responseClone = networkResponse.clone();
          caches.open(CACHE_NAME).then(function(cache) {
            cache.put('/', responseClone);
          });
        }
        return networkResponse;
      }).catch(function() {
        return caches.match('/').then(function(cached) {
          return cached || caches.match(event.request);
        });
      })
    );
    return;
  }

  event.respondWith(
    caches.match(event.request).then(function(response) {
      if (response) return response;
      return fetch(event.request).then(function(networkResponse) {
        // Cache successful responses
        if (networkResponse && networkResponse.status === 200) {
          var responseClone = networkResponse.clone();
          caches.open(CACHE_NAME).then(function(cache) {
            cache.put(event.request, responseClone);
          });
        }
        return networkResponse;
      }).catch(function() {
        return new Response('Offline', { status: 503 });
      });
    })
  );
});
";
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/javascript; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(sw)}\r\nCache-Control: max-age=0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(sw), ct);
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeServiceWorker error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        private async Task ServeIconAsync(TcpClient? tcpClient, Stream stream, string request, CancellationToken ct)
        {
            try
            {
                var parts = request.Split(' ');
                if (parts.Length < 2)
                {
                    await SendErrorAsync(stream, "400 Bad Request", ct);
                    return;
                }

                var name = parts[1].TrimStart('/');
                var query = name.IndexOf('?');
                if (query >= 0) name = name.Substring(0, query);
                if (name.StartsWith("icons/", StringComparison.OrdinalIgnoreCase)) name = name.Substring("icons/".Length);

                var pngData = WebRemoteIconProvider.GetIcon(name);
                if (pngData == null)
                {
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var header = $"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {pngData.Length}\r\nCache-Control: max-age=86400\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(pngData, ct);
                Logger.Debug($"[WebRemote] Icon served: {name} ({pngData.Length} bytes)");
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeIcon error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        private async Task ServeFaviconAsync(TcpClient? tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var icoData = WebRemoteIconProvider.ReadAsset("srcbox.ico")
                              ?? WebRemoteIconProvider.ReadAsset("docs", "public", "srcbox.ico");
                if (icoData == null)
                {
                    Logger.Warn("[WebRemote] Favicon not found");
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var header = $"HTTP/1.1 200 OK\r\nContent-Type: image/x-icon\r\nContent-Length: {icoData.Length}\r\nCache-Control: max-age=86400\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(icoData, ct);
                Logger.Debug($"[WebRemote] Favicon served ({icoData.Length} bytes)");
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeFavicon error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        /// <summary>
        /// Hands out the CA certificate. Phones install it once (over http, which needs no trust yet),
        /// after which the https address counts as a secure context and the app can be installed.
        /// </summary>
        private async Task ServeCertificateAsync(TcpClient tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var der = WebRemoteCertificateService.GetRootCertificateDer();
                if (der == null || der.Length == 0)
                {
                    Logger.Warn("[WebRemote] CA certificate requested but not available");
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-x509-ca-cert\r\nContent-Length: {der.Length}\r\nContent-Disposition: attachment; filename=\"srcbox-ca.crt\"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(der, ct);
                Logger.Debug($"[WebRemote] CA certificate served ({der.Length} bytes)");
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeCertificate error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        /// <summary>
        /// Path of the request line without the query string, used to route the certificate downloads.
        /// </summary>
        internal static string RequestPath(string requestLine)
        {
            if (string.IsNullOrEmpty(requestLine)) return "";
            var firstSpace = requestLine.IndexOf(' ');
            if (firstSpace < 0) return "";
            var rest = requestLine[(firstSpace + 1)..].Trim();
            var secondSpace = rest.IndexOf(' ');
            if (secondSpace >= 0) rest = rest[..secondSpace];
            var query = rest.IndexOf('?');
            if (query >= 0) rest = rest[..query];
            return rest;
        }

        /// <summary>
        /// Hands out the CA certificate as PEM text: a few phone installers refuse the DER file.
        /// </summary>
        private async Task ServeCertificatePemAsync(TcpClient tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var pem = WebRemoteCertificateService.GetRootCertificatePem();
                if (string.IsNullOrEmpty(pem))
                {
                    Logger.Warn("[WebRemote] CA certificate requested but not available");
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var body = Encoding.UTF8.GetBytes(pem);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-pem-file\r\nContent-Length: {body.Length}\r\nContent-Disposition: attachment; filename=\"srcbox-ca.pem\"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(body, ct);
                Logger.Debug($"[WebRemote] CA certificate (pem) served ({body.Length} bytes)");
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeCertificatePem error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        /// <summary>
        /// Hands out an Apple configuration profile that installs the CA. On iOS this is the reliable
        /// way in: a bare .crt download often ends up in the Files app with nothing able to install it.
        /// </summary>
        private async Task ServeCertificateProfileAsync(TcpClient tcpClient, Stream stream, CancellationToken ct)
        {
            try
            {
                var profile = WebRemoteCertificateService.BuildRootCaProfile();
                if (string.IsNullOrEmpty(profile))
                {
                    Logger.Warn("[WebRemote] CA profile requested but not available");
                    await SendErrorAsync(stream, "404 Not Found", ct);
                    return;
                }

                var body = Encoding.UTF8.GetBytes(profile);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-apple-aspen-config\r\nContent-Length: {body.Length}\r\nContent-Disposition: attachment; filename=\"srcbox-ca.mobileconfig\"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                await stream.WriteAsync(body, ct);
                Logger.Debug($"[WebRemote] CA profile served ({body.Length} bytes)");
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] ServeCertificateProfile error: {ex.Message}"); }
            finally { tcpClient.Close(); }
        }

        private async Task HandleWebSocketAsync(TcpClient tcpClient, Stream stream, string request, CancellationToken ct)
        {
            WebSocket? ws = null;
            try
            {
                // Parse WebSocket key for handshake
                var key = "";
                foreach (var line in request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("Sec-WebSocket-Key:"))
                    {
                        key = line.Substring(line.IndexOf(":") + 1).Trim();
                        break;
                    }
                }

                Logger.Debug($"[WebRemote] WebSocket key: {(string.IsNullOrEmpty(key) ? "NOT FOUND" : "found")}");

                if (string.IsNullOrEmpty(key))
                {
                    tcpClient.Close();
                    return;
                }

                // WebSocket handshake response
                var acceptKey = Convert.ToBase64String(System.Security.Cryptography.SHA1.Create()
                    .ComputeHash(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var handshake = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\nSec-WebSocket-Version: 13\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);

                // Create WebSocket from raw connection
                ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromMinutes(30));

                lock (_clientsLock) { _clients.Add(ws); }
                Logger.Debug($"[WebRemote] WebSocket client connected, state: {ws.State}");

                // Receive messages
                var receiveBuf = new byte[8192];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    try
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuf), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                            break;
                        }
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var msg = Encoding.UTF8.GetString(receiveBuf, 0, result.Count);
                            HandleMessage(ws, msg, ct);
                        }
                    }
                    catch (WebSocketException) { break; }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex) { Logger.Error($"[WebRemote] WebSocket error: {ex.Message}"); }
            finally
            {
                if (ws != null)
                {
                    lock (_clientsLock) { _clients.Remove(ws); }
                    // Force a fresh password prompt on reconnect instead of trusting a stale entry.
                    _authenticatedClients.TryRemove(ws, out _);
                }
                try { tcpClient.Close(); } catch { }
            }
        }

        private async void HandleMessage(WebSocket ws, string message, CancellationToken ct)
        {
            try
            {
                Logger.Debug($"[WebRemote] Received message: {message}");
                var json = JsonSerializer.Deserialize<JsonElement>(message);
                if (!json.TryGetProperty("action", out var actionElem)) return;
                var action = actionElem.GetString() ?? "";
                Logger.Debug($"[WebRemote] Action: {action}");

                // 密码验证：除了 auth 动作外都需要验证
                if (_requirePassword && action != "auth")
                {
                    bool isAuth = _authenticatedClients.ContainsKey(ws); // NEW-13: lock-free, per connection
                    if (!isAuth)
                    {
                        Logger.Warn($"[WebRemote] Unauthorized access attempt: {action}");
                        var unauthorizedResp = JsonSerializer.Serialize(new { error = "Unauthorized", requireAuth = true });
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(unauthorizedResp)), WebSocketMessageType.Text, true, ct);
                        return;
                    }
                }

                object? result = null;

                switch (action)
                {
                    case "auth":
                    {
                        string? pwd = null;
                        if (json.TryGetProperty("password", out var pwdElem))
                            pwd = pwdElem.GetString();
                        bool ok = !string.IsNullOrEmpty(_password) && pwd == _password;
                        if (ok)
                        {
                            _authenticatedClients.TryAdd(ws, true); // NEW-13: lock-free, per connection
                            Logger.Debug("[WebRemote] Client authenticated successfully");
                        }
                        else
                        {
                            // Never log the value itself: the redactor only masks url style secrets and this
                            // line used to write the password in clear text into the log file.
                            Logger.Warn("[WebRemote] 认证失败（口令不匹配）");
                        }
                        result = new { success = ok, requireAuth = _requirePassword };
                        break;
                    }

                    case "getStatus":
                        result = GetStatusCallback?.Invoke() ?? new WebRemoteStatus();
                        break;

                    case "getChannels":
                        var groups = GetChannelsCallback?.Invoke() ?? new List<WebRemoteChannelGroup>();
                        Logger.Debug($"[WebRemote] getChannels returned {groups.Count} groups");
                        if (groups.Count > 0)
                        {
                            Logger.Debug($"[WebRemote] First group: {groups[0].Name}, channels: {groups[0].Channels.Count}");
                        }
                        result = new { groups, favorites = groups.FirstOrDefault(g => g.IsFavorites)?.Channels ?? new List<WebRemoteChannel>() };
                        break;

                    case "getEpg":
                        var channelId = "";
                        DateTime? filterDate = null;
                        if (json.TryGetProperty("channelId", out var idElem))
                            channelId = idElem.GetString() ?? "";
                        if (json.TryGetProperty("date", out var dateElem) && dateElem.ValueKind == JsonValueKind.String)
                        {
                            var dateStr = dateElem.GetString();
                            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsed))
                                filterDate = parsed;
                        }
                        result = new { channelId, programs = GetEpgCallback?.Invoke(channelId, filterDate) ?? new List<WebRemoteProgram>() };
                        break;

                    case "play":
                        PlayCallback?.Invoke();
                        result = new { success = true };
                        break;

                    case "pause":
                        PauseCallback?.Invoke();
                        result = new { success = true };
                        break;

                    case "stop":
                        StopCallback?.Invoke();
                        result = new { success = true };
                        break;

                    case "volume":
                        if (json.TryGetProperty("volume", out var volElem))
                            SetVolumeCallback?.Invoke(volElem.GetDouble());
                        result = new { success = true };
                        break;

                    case "channel":
                        if (json.TryGetProperty("channelId", out var chIdElem))
                            ChangeChannelCallback?.Invoke(chIdElem.GetString() ?? "");
                        result = new { success = true };
                        break;

                    case "fullscreen":
                        result = new { success = true };
                        FullscreenCallback?.Invoke();
                        break;

                    case "switchSource":
                        result = new { success = true };
                        SwitchSourceCallback?.Invoke();
                        break;

                    case "exit":
                        result = new { success = true };
                        ExitCallback?.Invoke();
                        break;

                    case "getSources":
                        {
                            var sources = GetSourcesCallback?.Invoke() ?? new List<WebRemoteSource>();
                            result = new { sources };
                        }
                        break;

                    case "selectSource":
                        {
                            string? selName = null, selUrl = null;
                            if (json.TryGetProperty("name", out var sn)) selName = sn.GetString();
                            if (json.TryGetProperty("url", out var su)) selUrl = su.GetString();
                            SelectSourceCallback?.Invoke(selName ?? selUrl ?? "");
                            result = new { success = true };
                        }
                        break;

                    case "addSource":
                        {
                            string? aName = null, aUrl = null;
                            if (json.TryGetProperty("name", out var an)) aName = an.GetString();
                            if (json.TryGetProperty("url", out var au)) aUrl = au.GetString();
                            if (!string.IsNullOrEmpty(aName) && !string.IsNullOrEmpty(aUrl))
                                AddSourceCallback?.Invoke(aName, aUrl);
                            result = new { success = true };
                        }
                        break;

                    case "removeSource":
                        {
                            string? rKey = null;
                            if (json.TryGetProperty("name", out var rn)) rKey = rn.GetString();
                            if (json.TryGetProperty("url", out var ru)) rKey = rKey ?? ru.GetString();
                            RemoveSourceCallback?.Invoke(rKey ?? "");
                            result = new { success = true };
                        }
                        break;

                    case "prevSource":
                        SelectSourceCallback?.Invoke("__prev__");
                        result = new { success = true };
                        break;

                    case "nextSource":
                        SelectSourceCallback?.Invoke("__next__");
                        result = new { success = true };
                        break;

                    case "seek":
                        if (json.TryGetProperty("seconds", out var seekSec))
                            SeekCallback?.Invoke(seekSec.GetDouble());
                        result = new { success = true };
                        break;

                    case "setSpeed":
                        if (json.TryGetProperty("speed", out var speedVal))
                            SetSpeedCallback?.Invoke(speedVal.GetDouble());
                        result = new { success = true };
                        break;

                    case "setTimeshift":
                        if (json.TryGetProperty("enabled", out var tsEnabled))
                            SetTimeshiftCallback?.Invoke(tsEnabled.GetBoolean());
                        result = new { success = true };
                        break;

                    // Absolute scrub from the page: "seconds" is the offset from the start of the
                    // timeshift window. The window travels in getTimeshift so an open scrubber can
                    // follow the playback position without re-reading the whole status (and its EPG)
                    // on every tick.
                    case "seekTimeshift":
                        if (json.TryGetProperty("seconds", out var tsSeek))
                            SeekTimeshiftCallback?.Invoke(tsSeek.GetDouble());
                        result = new { success = true };
                        break;

                    case "getTimeshift":
                        result = new { timeshift = GetTimeshiftCallback?.Invoke() ?? new WebRemoteTimeshift() };
                        break;

                    case "replayProgram":
                        {
                            string? rpChannelId = null, rpTitle = null, rpStart = null, rpEnd = null;
                            if (json.TryGetProperty("channelId", out var rpcid)) rpChannelId = rpcid.GetString();
                            if (json.TryGetProperty("programTitle", out var rpt)) rpTitle = rpt.GetString();
                            if (json.TryGetProperty("start", out var rpstart)) rpStart = rpstart.GetString();
                            if (json.TryGetProperty("end", out var rpend)) rpEnd = rpend.GetString();
                            ReplayProgramCallback?.Invoke(rpChannelId ?? "", rpTitle ?? "", rpStart ?? "", rpEnd ?? "");
                            result = new { success = true };
                        }
                        break;

                    case "voiceCommand":
                        {
                            var voiceText = json.TryGetProperty("text", out var voiceTextElem)
                                ? voiceTextElem.GetString() ?? ""
                                : "";
                            var voiceLang = json.TryGetProperty("lang", out var voiceLangElem)
                                ? voiceLangElem.GetString() ?? ""
                                : "";
                            var voiceAction = json.TryGetProperty("command", out var voiceActionElem)
                                ? voiceActionElem.GetString() ?? ""
                                : "";
                            var voiceTarget = json.TryGetProperty("target", out var voiceTargetElem)
                                ? voiceTargetElem.GetString() ?? ""
                                : "";
                            if (string.IsNullOrWhiteSpace(voiceLang)) voiceLang = ParseAcceptLanguage("");
                            Logger.Debug($"[WebRemote] voiceCommand: \"{voiceText}\" ({voiceLang})");

                            if (VoiceCommandCallback == null)
                            {
                                result = new VoiceCommandReply
                                {
                                    Success = false,
                                    Verb = "unknown",
                                    Text = voiceText,
                                    Spoken = WebRemoteStrings.Get(voiceLang) is { } vs
                                             && vs.TryGetValue("voice_unsupported", out var unsupported)
                                        ? unsupported
                                        : "Voice control is not available"
                                };
                                break;
                            }

                            var voiceContext = new VoiceCommandContext
                            {
                                Text = string.IsNullOrWhiteSpace(voiceText) ? voiceTarget : voiceText,
                                Lang = voiceLang
                            };
                            result = VoiceCommandCallback(voiceContext, voiceAction, voiceTarget);
                        }
                        break;

                    case "getReminders":
                        {
                            var reminders = GetRemindersCallback?.Invoke() ?? new List<WebRemoteReminder>();
                            result = new { reminders };
                        }
                        break;

                    case "addReminder":
                        {
                            string remId = "", remChannelId = "", remStartAt = "", remEndTime = "", remAction = "", remTitle = "";
                            int remPreAlert = 0; int? remDur = null;
                            if (json.TryGetProperty("channelId", out var rcid)) remChannelId = rcid.GetString() ?? "";
                            if (json.TryGetProperty("startAt", out var rsa)) remStartAt = rsa.GetString() ?? "";
                            if (json.TryGetProperty("endTime", out var ret)) remEndTime = ret.GetString() ?? "";
                            if (json.TryGetProperty("action", out var rac)) remAction = rac.GetString() ?? "play";
                            if (json.TryGetProperty("programTitle", out var rpt2)) remTitle = rpt2.GetString() ?? "";
                            if (json.TryGetProperty("preAlertSeconds", out var rpa)) remPreAlert = rpa.GetInt32();
                            if (json.TryGetProperty("recordDurationMin", out var rdm) && rdm.ValueKind != JsonValueKind.Null) remDur = rdm.GetInt32();
                            remId = AddReminderCallback?.Invoke(remChannelId, remStartAt, remEndTime, remAction, remTitle, remPreAlert, remDur) ?? "";
                            result = new { success = !string.IsNullOrEmpty(remId), reminderId = remId };
                        }
                        break;

                    case "cancelReminder":
                        {
                            string? cancelId = null;
                            if (json.TryGetProperty("id", out var cid)) cancelId = cid.GetString();
                            CancelReminderCallback?.Invoke(cancelId ?? "");
                            result = new { success = true };
                        }
                        break;

                    case "getRecordings":
                        {
                            var recordings = GetRecordingsCallback?.Invoke() ?? new List<WebRemoteRecording>();
                            result = new { recordings };
                        }
                        break;

                    case "stopRecording":
                        {
                            string? stopId = null;
                            if (json.TryGetProperty("id", out var sid)) stopId = sid.GetString();
                            StopRecordingCallback?.Invoke(stopId ?? "");
                            result = new { success = true };
                        }
                        break;

                    case "deleteRecording":
                        {
                            string? delId = null;
                            if (json.TryGetProperty("id", out var did)) delId = did.GetString();
                            DeleteRecordingCallback?.Invoke(delId ?? "");
                            result = new { success = true };
                        }
                        break;

                    default:
                        result = new { error = "Unknown action" };
                        break;
                }

                var response = JsonSerializer.Serialize(result, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(response)), WebSocketMessageType.Text, true, ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] HandleMessage error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>Voice settings baked into the page when the caller passes no override.</summary>
        private static string DefaultVoiceJson => BuildVoiceJson(AppSettings.Current?.WebRemote?.Voice);

        /// <summary>
        /// Serializes the voice settings the page needs. Only these fields cross to the client; the
        /// custom rules and the endpoint token stay on the server.
        /// </summary>
        internal static string BuildVoiceJson(VoiceConfig? voice)
        {
            var payload = new Dictionary<string, object?>
            {
                ["enabled"] = voice?.Enabled ?? true,
                ["speak"] = voice?.SpeakBack ?? true,
                ["handsFree"] = voice?.HandsFree ?? false,
                ["lang"] = voice?.Lang ?? "",
                ["wake"] = voice?.WakeWords ?? new List<string>()
            };
            return JsonSerializer.Serialize(payload);
        }

        internal static string GetRemoteHtml(string lang = "zh-CN", int httpsPort = 0, bool? showChannelList = null, bool? showEpgList = null, string? voiceJson = null)
        {
            var strings = WebRemoteStrings.Get(lang);
            // The section switches come from the Web Remote settings; the parameters let tests override them.
            var webRemote = AppSettings.Current?.WebRemote;
            var showChannels = showChannelList ?? webRemote?.ShowChannelList ?? true;
            var showEpg = showEpgList ?? webRemote?.ShowEpgList ?? true;
            var stringsJson = System.Text.Json.JsonSerializer.Serialize(strings);
            // The html lang attribute has to follow the page language, Traditional Chinese included: the string
            // tables pick zh-TW from the same Accept-Language tag, so hard-coding zh-CN mislabelled that page.
            var langTag = (lang ?? "zh-CN").ToLowerInvariant();
            var langAttr = langTag.StartsWith("en") ? "en"
                : langTag.StartsWith("ru") ? "ru"
                : langTag.Contains("tw") || langTag.Contains("hk") || langTag.Contains("hant") ? "zh-Hant"
                : "zh-CN";
            var tpl = BuildHtmlTemplate()
                .Replace("__T_JSON__", stringsJson)
                .Replace("__LANG__", langAttr)
                .Replace("__TITLE__", strings.TryGetValue("title", out var t1) ? t1 : "SrcBox")
                .Replace("__PWD_TITLE__", strings.TryGetValue("pwd_title", out var t2) ? t2 : "Password")
                .Replace("__PWD_PH__", strings.TryGetValue("pwd_placeholder", out var t3) ? t3 : "")
                .Replace("__PWD_OK__", strings.TryGetValue("pwd_ok", out var t4) ? t4 : "OK")
                .Replace("__PWD_ERR__", strings.TryGetValue("pwd_error", out var t5) ? t5 : "")
                .Replace("__TITLE_BAR__", strings.TryGetValue("title", out var t6) ? t6 : "SrcBox")
                .Replace("__THEME__", strings.TryGetValue("theme_dark", out var t8) ? t8 : "")
                .Replace("__STATUS_STOPPED__", strings.TryGetValue("status_stopped", out var t9) ? t9 : "")
                .Replace("__BTN_FULLSCREEN__", strings.TryGetValue("btn_fullscreen", out var t10) ? t10 : "")
                .Replace("__BTN_PLAY__", strings.TryGetValue("btn_play", out var t11) ? t11 : "")
                .Replace("__BTN_PAUSE__", strings.TryGetValue("btn_pause", out var t12) ? t12 : "")
                .Replace("__BTN_STOP__", strings.TryGetValue("btn_stop", out var t13) ? t13 : "")
                .Replace("__BTN_MUTE__", strings.TryGetValue("btn_mute", out var t14) ? t14 : "")
                .Replace("__BTN_PREV__", strings.TryGetValue("btn_prev", out var t15) ? t15 : "")
                .Replace("__BTN_NEXT__", strings.TryGetValue("btn_next", out var t16) ? t16 : "")
                .Replace("__BTN_SWITCH__", strings.TryGetValue("btn_switch", out var t17) ? t17 : "")
                .Replace("__BTN_EXIT__", strings.TryGetValue("btn_exit", out var t18) ? t18 : "")
                .Replace("__BTN_REFRESH__", strings.TryGetValue("btn_refresh", out var t19) ? t19 : "")
                .Replace("__VIEW_STANDARD__", strings.TryGetValue("view_standard", out var tv1) ? tv1 : "Remote")
                .Replace("__VIEW_FULL__", strings.TryGetValue("view_full", out var tv2) ? tv2 : "Full")
                .Replace("__SEC_CHANNELS__", strings.TryGetValue("section_channels", out var t20) ? t20 : "")
                .Replace("__LOADING__", strings.TryGetValue("loading", out var t21) ? t21 : "")
                .Replace("__SEC_EPG__", strings.TryGetValue("section_epg", out var t22) ? t22 : "")
                .Replace("__EPG_SELECT__", strings.TryGetValue("epg_select", out var t23) ? t23 : "")
                .Replace("__FAV_STAR__", strings.TryGetValue("fav_star", out var t27) ? t27 : "*")
                .Replace("__FAV_GROUP__", strings.TryGetValue("fav_group", out var t28) ? t28 : "Favorites")
                .Replace("__SEC_SOURCES__", strings.TryGetValue("sec_sources", out var t29) ? t29 : "")
                .Replace("__SEC_REMINDERS__", strings.TryGetValue("sec_reminders", out var t30) ? t30 : "")
                .Replace("__SEC_RECORDINGS__", strings.TryGetValue("sec_recordings", out var t31) ? t31 : "")
                .Replace("__BTN_ADD_SOURCE__", strings.TryGetValue("btn_add_source", out var t32) ? t32 : "")
                .Replace("__MODAL_ADD_SOURCE__", strings.TryGetValue("modal_add_source", out var t33) ? t33 : "")
                .Replace("__LABEL_SOURCE_NAME__", strings.TryGetValue("label_source_name", out var t34) ? t34 : "")
                .Replace("__LABEL_SOURCE_URL__", strings.TryGetValue("label_source_url", out var t35) ? t35 : "")
                .Replace("__MODAL_ADD_REMINDER__", strings.TryGetValue("modal_add_reminder", out var t46) ? t46 : "")
                .Replace("__LABEL_REMINDER_ACTION__", strings.TryGetValue("label_reminder_action", out var t47) ? t47 : "")
                .Replace("__ACTION_PLAY__", strings.TryGetValue("action_play", out var t48) ? t48 : "")
                .Replace("__ACTION_RECORD_FRONT__", strings.TryGetValue("action_record_front", out var t49) ? t49 : "")
                .Replace("__ACTION_RECORD_BACK__", strings.TryGetValue("action_record_back", out var t50) ? t50 : "")
                .Replace("__LABEL_PRE_ALERT__", strings.TryGetValue("label_pre_alert", out var t52) ? t52 : "")
                .Replace("__BTN_CANCEL__", strings.TryGetValue("btn_cancel", out var t53) ? t53 : "")
                .Replace("__BTN_CONFIRM__", strings.TryGetValue("btn_confirm", out var t54) ? t54 : "")
                .Replace("__BTN_TIMESHIFT__", strings.TryGetValue("btn_timeshift", out var t56) ? t56 : "")
                // HTTPS port offered to the page for the "open over https" install step (0 = HTTPS disabled)
                .Replace("__HTTPS_PORT__", httpsPort.ToString())
                // Section switches are baked in so a section hidden in the settings is never shown
                .Replace("__SHOW_CHANNELS__", showChannels ? "true" : "false")
                .Replace("__SHOW_EPG__", showEpg ? "true" : "false")
                // Voice settings for the page; the tests and any caller that does not care get the defaults
                .Replace("__VOICE_JSON__", string.IsNullOrWhiteSpace(voiceJson) ? DefaultVoiceJson : voiceJson);
            return tpl;
        }

        private static string ParseAcceptLanguage(string request)
        {
            try
            {
                foreach (var line in request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("Accept-Language:", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = line.Substring(line.IndexOf(":") + 1).Trim();
                        var first = v.Split(',')[0].Trim();
                        return first;
                    }
                }
            }
            catch { }
            return "zh-CN";
        }

        private static string BuildHtmlTemplate()
        {
            return @"<!DOCTYPE html>
<html lang=""__LANG__"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover"">
<!-- PWA Meta Tags -->
<meta name=""description"" content=""SrcBox IPTV Remote Control"">
<link rel=""manifest"" href=""/manifest.json"">
<meta name=""theme-color"" content=""#d9dee5"">
<meta name=""apple-mobile-web-app-capable"" content=""yes"">
<meta name=""apple-mobile-web-app-status-bar-style"" content=""black-translucent"">
<meta name=""apple-mobile-web-app-title"" content=""SrcBox"">
<link rel=""apple-touch-icon"" sizes=""180x180"" href=""/icons/apple-touch-icon-180.png"">
<link rel=""apple-touch-icon-precomposed"" sizes=""180x180"" href=""/icons/apple-touch-icon-180.png"">
<link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
<title>__TITLE__</title>
<style>
:root {
--neu-bg: hsl(215 24% 90%);
--neu-surface: hsl(215 24% 90%);
--neu-surface2: hsl(217 30% 86%);
--neu-text: hsl(216 31% 17%);
--neu-text-muted: hsl(215 16% 40%);
--neu-border: hsl(215 24% 78%);
--neu-primary: hsl(246 96% 68%);
--neu-primary-fg: #fff;
--neu-danger: hsl(0 72% 51%);
--neu-radius: 0.75rem;
--neu-well: var(--neu-surface);
--neu-shadow-out: 9px 9px 18px rgba(163,177,198,0.75), -9px -9px 18px rgba(255,255,255,0.95);
--neu-shadow-in: inset 5px 5px 10px rgba(163,177,198,0.75), inset -5px -5px 10px rgba(255,255,255,0.95);
--neu-shadow-out-sm: 5px 5px 10px rgba(163,177,198,0.7), -5px -5px 10px rgba(255,255,255,0.92);
--neu-shadow-in-sm: inset 3px 3px 6px rgba(163,177,198,0.7), inset -3px -3px 6px rgba(255,255,255,0.92);
--neu-hi: inset 1px 1px 1px rgba(255,255,255,0.9), inset -1px -1px 1px rgba(163,177,198,0.45);
--neu-grad: linear-gradient(145deg, rgba(255,255,255,0.55), rgba(163,177,198,0.12));
--neu-grad-well: linear-gradient(145deg, rgba(163,177,198,0.35), rgba(255,255,255,0.55));
}
body.dark-theme {
--neu-bg: hsl(0 0% 4%);
--neu-surface: hsl(0 0% 11%);
--neu-surface2: hsl(0 0% 19%);
--neu-well: hsl(0 0% 6%);
--neu-text: hsl(0 0% 98%);
--neu-text-muted: hsl(0 0% 68%);
--neu-border: hsl(0 0% 26%);
--neu-primary: hsl(246 96% 68%);
--neu-primary-fg: #fff;
--neu-danger: hsl(0 72% 51%);
--neu-shadow-out: 9px 9px 18px rgba(0,0,0,0.95), -9px -9px 18px rgba(255,255,255,0.24);
--neu-shadow-in: inset 5px 5px 10px rgba(0,0,0,0.95), inset -5px -5px 10px rgba(255,255,255,0.16);
--neu-shadow-out-sm: 5px 5px 10px rgba(0,0,0,0.95), -5px -5px 10px rgba(255,255,255,0.24);
--neu-shadow-in-sm: inset 3px 3px 6px rgba(0,0,0,0.95), inset -3px -3px 6px rgba(255,255,255,0.16);
--neu-hi: inset 1px 1px 1px rgba(255,255,255,0.16), inset -1px -1px 1px rgba(0,0,0,0.7);
--neu-grad: linear-gradient(145deg, rgba(255,255,255,0.18), rgba(0,0,0,0.35));
--neu-grad-well: linear-gradient(145deg, rgba(0,0,0,0.5), rgba(255,255,255,0.07));
}
/* Dark theme polish: on a near-black page same-tone surfaces make every button
   vanish, so raised controls get a lighter fill than the card holding them, active
   states keep that fill and only flip their shadow, and recessed wells sit between
   the page and the card tone. Primary/danger buttons keep their own colours.
   Only background-color is set here so the depth gradients below still layer on. */
body.dark-theme .btn, body.dark-theme .theme-toggle, body.dark-theme .view-toggle,
body.dark-theme .channel-item, body.dark-theme .epg-item, body.dark-theme .epg-nav-btn,
body.dark-theme .source-item, body.dark-theme .source-item .sbtns button,
body.dark-theme .modal-box .mbtns button,
body.dark-theme .rp-btn, body.dark-theme .rp-vol-btn, body.dark-theme .rp-text-btn,
body.dark-theme .rp-num-btn { background-color: var(--neu-surface2); }
body.dark-theme .channel-item.active, body.dark-theme .source-item.active,
body.dark-theme .epg-item.current { background-color: var(--neu-surface2); }
body.dark-theme .rp-ok { background-color: var(--neu-primary); }
body.dark-theme .rp-num-btn.rp-num-ok { background-color: var(--neu-primary); }
body.dark-theme .modal-box .mbtns .mok { background-color: var(--neu-primary); }
body.dark-theme .volume-bar, body.dark-theme .rp-vol-track, body.dark-theme .rp-num-display,
body.dark-theme .epg-date-display, body.dark-theme .modal-box input, body.dark-theme .modal-box select,
body.dark-theme #passwordOverlay input { background-color: var(--neu-well); }
* { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
html { background: var(--neu-bg); }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: var(--neu-bg); color: var(--neu-text); min-height: 100vh; overflow-x: hidden; }
body.dark-theme, body.theme-dark { color: var(--neu-text); }
/* Safe-area insets: with viewport-fit=cover the page paints under the iPhone notch and the home
   indicator, so every edge that holds content adds the matching env() inset. Browsers without env()
   keep the plain padding declared first. */
.container { max-width: 100%; margin: 0 auto; padding: 12px; padding-top: calc(12px + env(safe-area-inset-top, 0px)); padding-right: calc(12px + env(safe-area-inset-right, 0px)); padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px)); padding-left: calc(12px + env(safe-area-inset-left, 0px)); }
/* Header: title on the left, one tidy row of controls on the right. The title is the only
   flexible part, so a long name ellipsizes instead of squeezing the buttons. */
.header { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-bottom: 12px; padding: 8px 12px; background: var(--neu-surface); color: var(--neu-text); border: none; border-radius: var(--neu-radius); box-shadow: var(--neu-shadow-out); }
.header h1 { flex: 1 1 auto; min-width: 0; font-size: 16px; margin: 0; display: flex; align-items: center; gap: 8px; color: var(--neu-text); }
.header-title { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
@keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.6; } }
.theme-toggle { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 8px 12px; cursor: pointer; font-size: 14px; color: var(--neu-text); transition: all 0.2s; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.theme-toggle:hover { box-shadow: var(--neu-shadow-out); }
.theme-toggle:active { box-shadow: var(--neu-shadow-in-sm); }
.now-playing { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 16px; margin-bottom: 12px; position: relative; overflow: hidden; box-shadow: var(--neu-shadow-out); }
.now-playing-info { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.now-playing-left { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.now-playing-channel-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.now-playing-channel { font-size: 20px; font-weight: bold; color: var(--neu-text); }
.now-playing-sep { color: var(--neu-text-muted); font-size: 16px; }
.now-playing-program { font-size: 14px; color: var(--neu-text-muted); }
.now-playing-program-time { font-size: 12px; color: var(--neu-text-muted); margin-left: 4px; }
.now-playing-time { font-size: 12px; color: var(--neu-text-muted); }
.now-playing-mode { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 10px; font-weight: bold; flex-shrink: 0; color: var(--neu-primary-fg); }
.mode-live { background: var(--neu-danger); }
.mode-replay { background: #ffa502; }
.mode-timeshift { background: #1e90ff; }
.mode-recording { background: #ff6b6b; }
.mode-local { background: #57606f; }
.mode-stopped { background: #57606f; }
.controls-wrapper { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; margin-bottom: 12px; box-shadow: var(--neu-shadow-out); }
.controls { display: grid; grid-template-columns: repeat(5, 1fr); gap: 8px; }
.controls + .controls { margin-top: 10px; }
.btn { min-height: 52px; padding: 12px 6px; border: none; border-radius: var(--neu-radius); cursor: pointer; transition: all 0.15s; background: var(--neu-surface); color: var(--neu-text); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.btn:active { transform: scale(0.98); background: var(--neu-surface); box-shadow: var(--neu-shadow-in); }
/* A latched mode (timeshift) keeps the plain face: the pressed-in shadow marks it as on and turning
   it off restores the raised face, without repainting the button in the accent colour. */
.btn.active { background: var(--neu-surface); color: var(--neu-text); box-shadow: var(--neu-shadow-in); }
.btn span { font-size: 10px; color: var(--neu-text); }
.btn.active span { color: var(--neu-text); }
/* Timeshift scrubber: the seekable window drawn as an EPG-timed track the user can drag. It only
   shows up while timeshift runs, and the remote pad view hides it with the rest of the controls. */
.ts-scrubber { margin-top: 12px; }
.ts-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 11px; color: var(--neu-text-muted); margin-bottom: 6px; }
.ts-clock { font-size: 12px; font-weight: 600; color: var(--neu-text); font-variant-numeric: tabular-nums; }
/* The meter copies the volume row above it exactly - 20px glyph, 10px gaps, 44px value - so the two
   bars line up column for column, and the bar itself keeps the volume bar's 12px slab. The row
   padding widens the touch target so a finger can still grab the thin bar. */
.ts-meter { display: flex; align-items: center; gap: 10px; padding: 6px 0; }
.ts-icon { flex: 0 0 auto; width: 20px; height: 20px; fill: var(--neu-text-muted); }
.ts-time { flex: 0 0 44px; width: 44px; text-align: right; font-size: 12px; font-weight: 600; color: var(--neu-text); font-variant-numeric: tabular-nums; }
.ts-track { position: relative; flex: 1 1 auto; min-width: 0; height: 12px; border: none; border-radius: 8px; background: var(--neu-surface); box-shadow: var(--neu-shadow-in-sm); cursor: pointer; overflow: hidden; touch-action: none; }
.ts-track:focus-visible { outline: 2px solid var(--neu-primary); outline-offset: 2px; }
.ts-axis { position: absolute; inset: 0; }
.ts-seg { position: absolute; top: 0; bottom: 0; border-left: 1px solid var(--neu-border); }
.ts-seg.current { background: var(--neu-primary); opacity: 0.15; }
.ts-fill { position: absolute; left: 0; top: 0; bottom: 0; width: 0; background: var(--neu-primary); opacity: 0.3; }
.ts-knob { position: absolute; top: 0; bottom: 0; left: 0; width: 3px; margin-left: -1px; background: var(--neu-primary); }
/* The tick labels sit under the bar only, so they are inset by exactly the glyph column and the value
   column of the row above: the first and last label line up with the ends of the bar. */
.ts-scale { display: flex; justify-content: space-between; gap: 4px; margin-top: 4px; padding: 0 54px 0 30px; font-size: 10px; color: var(--neu-text-muted); font-variant-numeric: tabular-nums; white-space: nowrap; overflow: hidden; }
/* The EPG band: the same programmes as the track, drawn as a swipeable timeline of readable blocks.
   Each block is as wide as its slot (flex-grow in minutes, with a floor so short items stay legible),
   carries its own start time and title, and the block holding the cursor is highlighted and kept
   centred while a playhead marks the exact position. Tapping a block seeks to that slot's start, so
   the phone gets both coarse (tap a programme) and fine (drag the track) control. */
.ts-band { position: relative; margin-top: 8px; overflow-x: auto; overflow-y: hidden; -webkit-overflow-scrolling: touch; scrollbar-width: none; }
.ts-band::-webkit-scrollbar { display: none; }
.ts-band-inner { position: relative; display: flex; align-items: stretch; gap: 4px; min-height: 56px; }
.ts-block { flex: 1 1 0; min-width: 84px; padding: 6px 8px 8px; border: none; border-radius: 10px; background: var(--neu-surface); color: var(--neu-text); box-shadow: var(--neu-shadow-out-sm); text-align: left; cursor: pointer; touch-action: manipulation; display: flex; flex-direction: column; gap: 2px; }
.ts-block.current { box-shadow: var(--neu-shadow-in); }
.ts-block-time { font-size: 10px; color: var(--neu-text-muted); font-variant-numeric: tabular-nums; }
.ts-block-name { font-size: 12px; font-weight: 600; line-height: 1.25; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.ts-block.current .ts-block-name { color: var(--neu-primary); }
.ts-playhead { position: absolute; top: 0; bottom: 0; width: 2px; margin-left: -1px; background: var(--neu-primary); box-shadow: 0 0 6px var(--neu-primary); pointer-events: none; }
.ts-detail { display: flex; flex-direction: column; gap: 2px; margin-top: 6px; }
.ts-detail-main { font-size: 13px; font-weight: 600; color: var(--neu-text); }
.ts-detail-sub { font-size: 11px; color: var(--neu-text-muted); font-variant-numeric: tabular-nums; }
.icon { width: 20px; height: 20px; fill: currentColor; }
.volume-icon { width: 20px; height: 20px; font-size: 20px; fill: currentColor; color: var(--neu-text-muted); flex-shrink: 0; }
.volume-control { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; margin-bottom: 12px; display: flex; align-items: center; gap: 10px; box-shadow: var(--neu-shadow-out); }
.volume-bar { flex: 1; height: 12px; background: var(--neu-surface); border: none; border-radius: 8px; cursor: pointer; position: relative; overflow: hidden; box-shadow: var(--neu-shadow-in-sm); touch-action: manipulation; }
.volume-fill { height: 100%; background: var(--neu-primary); border-radius: 8px; transition: width 0.3s; box-shadow: var(--neu-shadow-out-sm); }
.volume-value { min-width: 44px; text-align: right; font-size: 12px; color: var(--neu-text); font-weight: 600; }
.channel-section { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; margin-bottom: 12px; box-shadow: var(--neu-shadow-out); }
.section-title { font-size: 12px; color: var(--neu-text-muted); margin-bottom: 10px; text-transform: uppercase; letter-spacing: 1px; }
.channel-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; max-height: 320px; overflow-y: auto; }
.channel-item { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 10px 12px; text-align: left; cursor: pointer; transition: all 0.2s; display: flex; align-items: center; gap: 10px; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; min-width: 0; }
.channel-item:hover { box-shadow: var(--neu-shadow-out); }
.channel-item.active { background: var(--neu-surface); border: none; box-shadow: var(--neu-shadow-in); }
.channel-item .logo { font-size: 26px; flex-shrink: 0; height: 56px; width: 80px; display: flex; align-items: center; justify-content: center; border-radius: 10px; color: var(--neu-text-muted); }
.channel-item .logo img { height: 56px; width: auto; max-width: 80px; object-fit: contain; }
.channel-item .info { flex: 1; min-width: 0; }
.channel-item .name { font-size: 13px; color: var(--neu-text); font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.channel-item .program { font-size: 11px; color: var(--neu-primary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-top: 2px; }
.channel-item .time { font-size: 10px; color: var(--neu-text-muted); margin-left: auto; flex-shrink: 0; white-space: nowrap; }
.epg-section { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; box-shadow: var(--neu-shadow-out); }
.epg-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px; }
.epg-header-title { font-size: 12px; color: var(--neu-text-muted); text-transform: uppercase; letter-spacing: 1px; }
.epg-channel-name { font-size: 14px; color: var(--neu-text); font-weight: bold; }
.epg-date-nav { display: flex; align-items: center; gap: 6px; }
.epg-nav-btn { background: var(--neu-surface); border: none; color: var(--neu-text); padding: 4px 10px; cursor: pointer; border-radius: 8px; font-size: 12px; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-nav-btn:hover { box-shadow: var(--neu-shadow-out); }
.epg-nav-btn:active { box-shadow: var(--neu-shadow-in-sm); }
.epg-date-display { font-size: 13px; color: var(--neu-text); cursor: pointer; padding: 4px 10px; min-width: 50px; text-align: center; border-radius: 8px; box-shadow: var(--neu-shadow-in-sm); }
.epg-timeline { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; font-size: 11px; color: var(--neu-text-muted); }
.epg-current-time { color: var(--neu-danger); font-weight: bold; }
.epg-list { max-height: 250px; overflow-y: auto; }
.epg-item { display: flex; align-items: center; background: var(--neu-surface); border: none; border-radius: 10px; margin-bottom: 8px; overflow: hidden; cursor: pointer; transition: all 0.2s; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-item:hover { box-shadow: var(--neu-shadow-out); }
.epg-item.current { background: var(--neu-surface); border-left: 3px solid var(--neu-primary); box-shadow: var(--neu-shadow-in); }
.epg-item.past { opacity: 0.5; }
.epg-item-live { border-bottom: 3px solid var(--neu-danger); }
.epg-item-replay { border-bottom: 3px solid var(--neu-primary); }
.epg-item-reminder { border-bottom: 3px solid #1e90ff; }
.epg-time { width: 80px; min-width: 70px; flex-shrink: 0; padding: 10px; font-size: 11px; color: var(--neu-text-muted); display: flex; flex-direction: column; justify-content: center; }
.epg-time-end { color: var(--neu-text-muted); font-size: 10px; margin-top: 2px; }
.epg-content { flex: 1; padding: 10px; font-size: 13px; color: var(--neu-text); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.epg-item .epg-time { min-width: 70px; }
.epg-item .epg-content { flex: 1; }
.epg-right-col { width: 80px; flex-shrink: 0; display: flex; align-items: center; justify-content: flex-end; gap: 4px; padding: 10px; }
.epg-right-area { display: flex; align-items: center; justify-content: flex-end; }
.epg-actions-row { display: flex; gap: 4px; align-items: center; margin-right: 4px; }
.epg-name { font-size: 13px; color: var(--neu-text); font-weight: 500; }
.epg-desc { font-size: 10px; color: var(--neu-text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.epg-badge { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 10px; font-weight: bold; margin-left: 4px; color: var(--neu-primary-fg); }
.epg-badge-live { background: var(--neu-danger); color: var(--neu-primary-fg); }
.epg-badge-replay { background: var(--neu-primary); color: var(--neu-primary-fg); }
.epg-badge-reminder { background: #1e90ff; color: var(--neu-primary-fg); }
.epg-badge-next { box-shadow: var(--neu-shadow-in-sm); }
.epg-btn-live { background: var(--neu-danger); border: none; border-radius: 8px; padding: 4px 8px; font-size: 10px; color: var(--neu-primary-fg); cursor: pointer; font-weight: bold; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-btn-replay { background: var(--neu-primary); border: none; border-radius: 8px; padding: 4px 8px; font-size: 10px; color: var(--neu-primary-fg); cursor: pointer; font-weight: bold; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-btn-timeshift { background: #1e90ff; border: none; border-radius: 8px; padding: 4px 8px; font-size: 9px; color: var(--neu-primary-fg); cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-btn-remind { background: #1e90ff; border: none; border-radius: 8px; padding: 4px 8px; font-size: 10px; color: var(--neu-primary-fg); cursor: pointer; font-weight: bold; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-btn-rec { background: #ffa502; border: none; border-radius: 8px; padding: 4px 8px; font-size: 9px; color: var(--neu-primary-fg); cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.epg-btn-live:active, .epg-btn-replay:active, .epg-btn-timeshift:active, .epg-btn-remind:active, .epg-btn-rec:active { box-shadow: var(--neu-shadow-in-sm); }
.loading { text-align: center; padding: 30px; color: var(--neu-text-muted); font-size: 12px; }
.loading::after { content: '...'; animation: dots 1.5s infinite; }
@keyframes dots { 0%,20% { content: '.'; } 40% { content: '..'; } 60%,100% { content: '...'; } }
#passwordOverlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: var(--neu-bg); display: flex; align-items: center; justify-content: center; z-index: 9999; flex-direction: column; gap: 16px; padding: 16px; padding-top: calc(16px + env(safe-area-inset-top, 0px)); padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px)); box-sizing: border-box; }
#passwordOverlay h2 { color: var(--neu-text); font-size: 18px; margin: 0; }
#passwordOverlay input { padding: 12px 16px; font-size: 14px; border: none; border-radius: var(--neu-radius); width: 200px; text-align: center; background: var(--neu-surface); color: var(--neu-text); box-shadow: var(--neu-shadow-in); }
#passwordOverlay button { padding: 10px 24px; font-size: 14px; border: none; border-radius: var(--neu-radius); background: var(--neu-primary); color: var(--neu-primary-fg); cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
#passwordOverlay button:hover { box-shadow: var(--neu-shadow-out); }
#passwordOverlay button:active { box-shadow: var(--neu-shadow-in-sm); }
#passwordOverlay .error { color: var(--neu-danger); font-size: 12px; display: none; }
.source-section { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; margin-bottom: 12px; box-shadow: var(--neu-shadow-out); }
.source-item { display: flex; align-items: center; justify-content: space-between; background: var(--neu-surface); border-radius: var(--neu-radius); padding: 10px 12px; margin-bottom: 8px; cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.source-item:hover { box-shadow: var(--neu-shadow-out); }
.source-item.active { background: var(--neu-surface); border: none; box-shadow: var(--neu-shadow-in); }
.source-item .sname { font-size: 12px; color: var(--neu-text); }
.source-item .surl { font-size: 10px; color: var(--neu-text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 200px; }
.source-item .sactive { color: var(--neu-primary); font-size: 10px; }
.source-item .sbtns { display: flex; gap: 6px; }
.source-item .sbtns button { padding: 6px 10px; font-size: 10px; background: var(--neu-surface); border: none; border-radius: 8px; color: var(--neu-text); cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.source-item .sbtns button:hover { box-shadow: var(--neu-shadow-out); }
.source-item .sbtns button:active { box-shadow: var(--neu-shadow-in-sm); }
.reminder-section, .recording-section { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 12px; margin-bottom: 12px; box-shadow: var(--neu-shadow-out); }
.reminder-item, .recording-item { display: flex; align-items: center; justify-content: space-between; background: var(--neu-surface); border-radius: var(--neu-radius); padding: 10px 12px; margin-bottom: 8px; box-shadow: var(--neu-shadow-out-sm); }
.reminder-item .rtime { font-size: 11px; color: var(--neu-text-muted); min-width: 120px; }
.reminder-item .rprogram { font-size: 12px; color: var(--neu-text); flex: 1; }
.reminder-item .raction { font-size: 10px; padding: 2px 6px; border-radius: 6px; color: var(--neu-primary-fg); }
.reminder-item .raction.play { background: var(--neu-primary); }
.reminder-item .raction.record_front { background: #ffa502; }
.reminder-item .raction.record_back { background: var(--neu-danger); }
.reminder-item .raction.notify { background: #57606f; }
.recording-item .rcstatus { font-size: 11px; color: var(--neu-text-muted); }
.recording-item .rcsize { font-size: 11px; color: var(--neu-primary); }
.modal-overlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: var(--neu-bg); opacity: 0.98; display: none; align-items: center; justify-content: center; z-index: 10000; padding: 16px; padding-top: calc(16px + env(safe-area-inset-top, 0px)); padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px)); box-sizing: border-box; }
.modal-overlay.show { display: flex; }
.modal-box { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 20px; min-width: 300px; max-width: 400px; box-shadow: var(--neu-shadow-out); max-height: calc(100vh - 32px); max-height: calc(100dvh - 32px); max-height: calc(100dvh - 32px - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px)); overflow-y: auto; -webkit-overflow-scrolling: touch; }
.modal-box h3 { margin: 0 0 16px 0; font-size: 16px; color: var(--neu-text); }
.modal-box label { display: block; font-size: 12px; color: var(--neu-text-muted); margin-bottom: 6px; }
.modal-box input, .modal-box select { width: 100%; padding: 10px 12px; border: none; border-radius: 10px; background: var(--neu-surface); color: var(--neu-text); font-size: 13px; margin-bottom: 12px; box-sizing: border-box; box-shadow: var(--neu-shadow-in-sm); appearance: none; -webkit-appearance: none; }
.modal-box .mbtns { display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px; }
.modal-box .mbtns button { padding: 10px 16px; border: none; border-radius: 10px; cursor: pointer; font-size: 13px; background: var(--neu-surface); color: var(--neu-text); box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.modal-box .mbtns button:active { box-shadow: var(--neu-shadow-in-sm); }
.modal-box .mbtns .mcancel { background: var(--neu-surface2); color: var(--neu-text); }
.modal-box .mbtns .mok { background: var(--neu-primary); color: var(--neu-primary-fg); }
.modal-box .rem-info { font-size: 13px; color: var(--neu-text); margin-bottom: 8px; }
/* Voice control overlay: reuses the neumorphic tokens so it matches both themes */
.voice-overlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: var(--neu-bg); opacity: 0.98; display: none; align-items: center; justify-content: center; z-index: 10050; padding: 16px; padding-top: calc(16px + env(safe-area-inset-top, 0px)); padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px)); box-sizing: border-box; }
.voice-overlay.show { display: flex; }
.voice-box { background: var(--neu-surface); border-radius: var(--neu-radius); padding: 18px; width: 100%; max-width: 420px; box-shadow: var(--neu-shadow-out); max-height: calc(100dvh - 32px - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px)); overflow-y: auto; -webkit-overflow-scrolling: touch; box-sizing: border-box; }
.voice-head { display: flex; align-items: center; justify-content: space-between; font-size: 15px; color: var(--neu-text); margin-bottom: 12px; }
.voice-x { border: none; background: var(--neu-surface2); color: var(--neu-text); border-radius: 50%; width: 30px; height: 30px; font-size: 14px; cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.voice-mic { display: block; margin: 4px auto 12px auto; width: 96px; height: 96px; border-radius: 50%; border: none; font-size: 40px; line-height: 1; background: var(--neu-surface); color: var(--neu-text); box-shadow: var(--neu-shadow-out); cursor: pointer; touch-action: manipulation; user-select: none; -webkit-user-select: none; }
.voice-mic:active, .voice-mic.listening { box-shadow: var(--neu-shadow-in); }
.voice-mic.listening { background: var(--neu-primary); color: var(--neu-primary-fg); animation: voicePulse 1.4s ease-in-out infinite; }
@keyframes voicePulse { 0%, 100% { transform: scale(1); } 50% { transform: scale(1.06); } }
.voice-status { text-align: center; font-size: 13px; color: var(--neu-text); margin-bottom: 10px; min-height: 18px; }
.voice-heard { text-align: center; font-size: 12px; color: var(--neu-text-muted); margin-bottom: 10px; word-break: break-word; }
.voice-options { display: flex; flex-direction: column; gap: 8px; margin-bottom: 10px; }
.voice-option { text-align: left; border: none; border-radius: 10px; padding: 10px 12px; background: var(--neu-surface); color: var(--neu-text); font-size: 13px; cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.voice-option:active { box-shadow: var(--neu-shadow-in-sm); }
.voice-option .vo-sub { display: block; font-size: 11px; color: var(--neu-text-muted); margin-top: 2px; }
.voice-row { display: flex; gap: 8px; margin-bottom: 10px; }
.voice-row input { flex: 1; min-width: 0; padding: 10px 12px; border: none; border-radius: 10px; background: var(--neu-surface); color: var(--neu-text); font-size: 13px; box-shadow: var(--neu-shadow-in-sm); box-sizing: border-box; -webkit-appearance: none; appearance: none; }
.voice-row button { padding: 10px 14px; border: none; border-radius: 10px; background: var(--neu-primary); color: var(--neu-primary-fg); font-size: 13px; cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.voice-foot { display: flex; align-items: center; gap: 8px; font-size: 11px; color: var(--neu-text-muted); }
.voice-foot button { padding: 6px 10px; border: none; border-radius: 8px; background: var(--neu-surface2); color: var(--neu-text-muted); font-size: 11px; cursor: pointer; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; white-space: nowrap; }
.voice-foot button.on { background: var(--neu-primary); color: var(--neu-primary-fg); }
::-webkit-scrollbar { width: 6px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: var(--neu-border); border-radius: 3px; }
.remote-pad { display: none; }
body.remote-mode .remote-pad { display: block; }
body.remote-mode .now-playing, body.remote-mode .controls-wrapper, body.remote-mode .volume-control, body.remote-mode .channel-section, body.remote-mode .epg-section, body.remote-mode #reminderSection, body.remote-mode #recordingSection { display: none; }
.header-actions { flex: 0 0 auto; display: flex; align-items: center; gap: 6px; }
/* Every control in the header shares one height; icon-only controls (theme / fullscreen /
   voice) are square so the row keeps a single rhythm next to the wider text buttons. */
.header-actions > button { min-height: 36px; padding: 0 12px; display: inline-flex; align-items: center; justify-content: center; font-size: 14px; line-height: 1; white-space: nowrap; }
.header-actions > button.icon-toggle { width: 36px; min-width: 36px; padding: 0; font-size: 16px; }
.header-actions .view-toggle { padding: 0 14px; font-weight: 600; }
.header-actions .install-toggle { padding: 0 10px; font-size: 13px; }
.view-toggle { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 8px 12px; cursor: pointer; font-size: 14px; color: var(--neu-text); transition: all 0.2s; box-shadow: var(--neu-shadow-out-sm); touch-action: manipulation; }
.ch-number { display: inline-block; min-width: 22px; margin-right: 6px; padding: 1px 6px; border-radius: 8px; background: var(--neu-surface2); color: var(--neu-text-muted); font-size: 11px; font-weight: 700; text-align: center; box-shadow: var(--neu-shadow-in-sm); }
.channel-item.active .ch-number { background: var(--neu-primary); color: var(--neu-primary-fg); }
.view-toggle:hover { box-shadow: var(--neu-shadow-out); }
.view-toggle:active { box-shadow: var(--neu-shadow-in-sm); }
.rp-status { background: var(--neu-surface); border: none; border-radius: var(--neu-radius); padding: 16px; margin-bottom: 16px; text-align: center; box-shadow: var(--neu-shadow-in); }
.rp-channel { font-size: 20px; font-weight: 700; color: var(--neu-text); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.rp-program { font-size: 13px; color: var(--neu-text-muted); margin-top: 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.rp-mode { display: inline-block; margin-top: 8px; padding: 2px 10px; border-radius: 10px; font-size: 10px; font-weight: bold; letter-spacing: 1px; color: var(--neu-primary-fg); background: var(--neu-primary); }
.rp-dpad { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin-bottom: 16px; }
.rp-btn { min-height: 64px; min-width: 64px; border: none; border-radius: var(--neu-radius); background: var(--neu-surface); color: var(--neu-text); font-size: 20px; display: flex; align-items: center; justify-content: center; cursor: pointer; transition: all 0.15s; box-shadow: var(--neu-shadow-out); touch-action: manipulation; -webkit-user-select: none; user-select: none; }
.rp-btn:active { box-shadow: var(--neu-shadow-in); }
.rp-up { grid-area: 1/2; }
.rp-left { grid-area: 2/1; }
.rp-ok { grid-area: 2/2; background: var(--neu-primary); color: var(--neu-primary-fg); font-weight: 700; font-size: 18px; box-shadow: var(--neu-shadow-out); }
.rp-ok:active { box-shadow: var(--neu-shadow-in-sm); }
.rp-right { grid-area: 2/3; }
.rp-down { grid-area: 3/2; }
.rp-vol { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
.rp-vol-btn { width: 64px; min-width: 64px; min-height: 64px; border: none; border-radius: var(--neu-radius); background: var(--neu-surface); color: var(--neu-text); font-size: 24px; display: flex; align-items: center; justify-content: center; cursor: pointer; transition: all 0.15s; box-shadow: var(--neu-shadow-out); touch-action: manipulation; }
.rp-vol-btn:active { box-shadow: var(--neu-shadow-in); }
.rp-vol-track { flex: 1; height: 14px; border-radius: 10px; background: var(--neu-surface); overflow: hidden; box-shadow: var(--neu-shadow-in-sm); }
.rp-vol-fill { height: 100%; background: var(--neu-primary); border-radius: 10px; transition: width 0.3s; }
.rp-footer { display: flex; gap: 8px; }
.rp-text-btn { flex: 1; min-height: 64px; border: none; border-radius: var(--neu-radius); background: var(--neu-surface); color: var(--neu-text); font-size: 14px; font-weight: 600; display: flex; align-items: center; justify-content: center; cursor: pointer; transition: all 0.15s; box-shadow: var(--neu-shadow-out); touch-action: manipulation; }
.rp-text-btn:active { box-shadow: var(--neu-shadow-in); }
.rp-numpad { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin-bottom: 12px; }
.rp-num-btn { min-height: 56px; border: none; border-radius: var(--neu-radius); background: var(--neu-surface); color: var(--neu-text); font-size: 22px; font-weight: 600; display: flex; align-items: center; justify-content: center; cursor: pointer; transition: all 0.15s; box-shadow: var(--neu-shadow-out); touch-action: manipulation; -webkit-user-select: none; user-select: none; }
.rp-num-btn:active { box-shadow: var(--neu-shadow-in); }
.rp-num-btn.rp-num-ok { background: var(--neu-primary); color: var(--neu-primary-fg); }
.rp-num-display { min-height: 38px; display: flex; align-items: center; justify-content: center; margin-bottom: 12px; border-radius: var(--neu-radius); background: var(--neu-surface); color: var(--neu-text); font-size: 22px; font-weight: 700; letter-spacing: 6px; box-shadow: var(--neu-shadow-in); }
.rp-num-display.rp-num-empty { color: var(--neu-text-muted); font-size: 12px; font-weight: 400; letter-spacing: 0; }
.rp-num-hint { min-height: 18px; margin-bottom: 16px; text-align: center; font-size: 12px; color: var(--neu-text-muted); }
@media (max-width: 400px) {
.controls { grid-template-columns: repeat(5, 1fr); gap: 4px; }
.btn { padding: 10px 4px; font-size: 16px; }
}
@media (max-width: 380px) {
.controls { gap: 4px; }
.btn { min-height: 48px; padding: 8px 2px; }
.epg-right-col { width: 62px; padding: 6px; }
.epg-time { width: 62px; min-width: 62px; padding: 8px 6px; }
.modal-box { min-width: 0; padding: 16px; }
.remote-pad { padding: 8px 10px; }
.rp-dpad { gap: 8px; }
.rp-btn { min-height: 56px; min-width: 56px; font-size: 18px; }
.rp-vol-btn { width: 56px; min-width: 56px; min-height: 56px; font-size: 20px; }
.rp-text-btn { min-height: 56px; font-size: 13px; }
.rp-channel { font-size: 17px; }
.rp-status { padding: 12px; margin-bottom: 12px; }
.rp-numpad { gap: 8px; }
.rp-num-btn { min-height: 48px; font-size: 19px; }
.rp-num-display { min-height: 34px; font-size: 19px; }
}
@media (min-width: 480px) {
.remote-pad { max-width: 420px; margin: 0 auto; }
}
/* Depth: translucent face gradients plus a bright top-left rim make the neumorphic
   relief read on phone screens; recessed wells take the inverse gradient. Accent
   buttons keep their own fill because the gradient overlay is translucent. */
.rp-footer .rp-text-btn.rp-exit { background-color: var(--neu-danger); color: var(--neu-primary-fg); }
.header, .now-playing, .controls-wrapper, .volume-control, .channel-section, .epg-section,
.source-section, .reminder-section, .recording-section, .modal-box,
.btn, .theme-toggle, .view-toggle, .rp-btn, .rp-vol-btn, .rp-text-btn,
.modal-box .mbtns button, #passwordOverlay button { background-image: var(--neu-grad); box-shadow: var(--neu-shadow-out), var(--neu-hi); }
.channel-item, .epg-item, .epg-nav-btn, .source-item, .source-item .sbtns button, .rp-num-btn,
.epg-btn-live, .epg-btn-replay, .epg-btn-timeshift, .epg-btn-remind, .epg-btn-rec { background-image: var(--neu-grad); box-shadow: var(--neu-shadow-out-sm), var(--neu-hi); }
.volume-bar, .rp-vol-track, .rp-num-display, .epg-date-display, .rp-status, .modal-box input,
.modal-box select, #passwordOverlay input { background-image: var(--neu-grad-well); }
/* Phone sized standard remote: the pad must fit one viewport without scrolling. Blocks scale
   with vh clamps so the layout adapts from a 360x640 phone up to a tablet. */
.install-toggle { font-size: 13px; padding: 8px 10px; }
.install-body { line-height: 1.5; word-break: break-word; }
.install-body a { color: var(--neu-primary); text-decoration: underline; }
body.remote-mode { height: 100vh; height: 100dvh; overflow-x: hidden; overflow-y: auto; display: flex; flex-direction: column; --safe-vh: calc((100vh - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px)) / 100); }
/* --safe-vh is 1% of the height the pad can really use: the viewport minus the safe-area insets the
   container reserves, so the whole pad still fits on a notched phone instead of pushing the footer out. */
@supports (height: 100dvh) { body.remote-mode { --safe-vh: calc((100dvh - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px)) / 100); } }
body.remote-mode .container { flex: 1; min-height: 0; display: flex; flex-direction: column; padding: clamp(6px, calc(var(--safe-vh) * 1.2), 12px); padding-top: max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-top, 0px)); padding-right: max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-right, 0px)); padding-bottom: max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-bottom, 0px)); padding-left: max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-left, 0px)); }
body.remote-mode .header { flex: 0 0 auto; margin-bottom: clamp(6px, calc(var(--safe-vh) * 1.2), 12px); }
body.remote-mode .remote-pad { flex: 1; min-height: 0; display: flex; flex-direction: column; justify-content: space-between; gap: clamp(4px, calc(var(--safe-vh) * 0.9), 12px); max-width: none; }
body.remote-mode .rp-status { padding: clamp(8px, calc(var(--safe-vh) * 1.4), 16px); margin-bottom: 0; }
body.remote-mode .rp-channel { font-size: clamp(15px, calc(var(--safe-vh) * 2.6), 20px); }
body.remote-mode .rp-program { font-size: clamp(11px, calc(var(--safe-vh) * 1.7), 13px); }
body.remote-mode .rp-dpad { gap: clamp(6px, calc(var(--safe-vh) * 1.2), 12px); margin-bottom: 0; }
body.remote-mode .rp-btn { min-height: clamp(36px, calc(var(--safe-vh) * 6.6), 64px); min-width: 0; font-size: clamp(15px, calc(var(--safe-vh) * 2.4), 20px); }
body.remote-mode .rp-vol { gap: clamp(6px, calc(var(--safe-vh) * 1.2), 12px); margin-bottom: 0; }
body.remote-mode .rp-vol-btn { min-height: clamp(36px, calc(var(--safe-vh) * 6.6), 64px); min-width: clamp(36px, calc(var(--safe-vh) * 6.6), 64px); }
body.remote-mode .rp-numpad { gap: clamp(6px, calc(var(--safe-vh) * 1.2), 12px); margin-bottom: 0; }
body.remote-mode .rp-num-btn { min-height: clamp(30px, calc(var(--safe-vh) * 5.2), 56px); font-size: clamp(15px, calc(var(--safe-vh) * 2.4), 22px); }
body.remote-mode .rp-num-display { min-height: clamp(26px, calc(var(--safe-vh) * 4.2), 38px); margin-bottom: clamp(4px, calc(var(--safe-vh) * 0.8), 12px); }
body.remote-mode .rp-num-hint { min-height: clamp(12px, calc(var(--safe-vh) * 2), 18px); margin-bottom: 0; }
body.remote-mode .rp-footer { gap: clamp(6px, calc(var(--safe-vh) * 1.2), 8px); }
body.remote-mode .rp-text-btn { min-height: clamp(36px, calc(var(--safe-vh) * 6.6), 64px); font-size: clamp(11px, calc(var(--safe-vh) * 1.7), 14px); }
@media (min-width: 480px) { body.remote-mode .remote-pad { max-width: 420px; margin: 0 auto; width: 100%; } }
</style>
</head>
<body>
<div id=""passwordOverlay"" style=""display:none;"">
<h2>__PWD_TITLE__</h2>
<input type=""password"" id=""pwdInput"" placeholder=""__PWD_PH__"" onkeydown=""if(event.key==='Enter')doAuth()""/>
<button onclick=""doAuth()"">__PWD_OK__</button>
<div class=""error"" id=""authError"">__PWD_ERR__</div>
</div>
<div class=""container"">
<div class=""header"">
<h1><span class=""header-title"">__TITLE_BAR__</span></h1>
<div class=""header-actions"">
<button class=""view-toggle"" id=""viewToggle"" onclick=""toggleRemoteView()"" data-standard=""__VIEW_STANDARD__"" data-full=""__VIEW_FULL__"">__VIEW_STANDARD__</button>
<button class=""theme-toggle install-toggle"" id=""installFab"" onclick=""showInstallModal()"" style=""display:none;""></button>
<button class=""theme-toggle icon-toggle"" id=""fsFab"" onclick=""toggleBrowserFullscreen()"" style=""display:none;"">⛶</button>
<button class=""theme-toggle icon-toggle"" id=""themeToggle"" onclick=""toggleTheme()"" title=""__THEME__"" aria-label=""__THEME__"">🌙</button>
<button class=""theme-toggle icon-toggle"" id=""voiceFab"" onclick=""toggleVoice()"" style=""display:none;"">🎙</button>
</div>
</div>

<!-- Standard remote view (D-pad / volume / number keys) -->
<div class=""remote-pad"" id=""remotePad"">
<div class=""rp-status"">
<div class=""rp-channel"" id=""rpChannel"">-</div>
<div class=""rp-program"" id=""rpProgram""></div>
<span class=""rp-mode mode-stopped"" id=""rpMode"">__STATUS_STOPPED__</span>
</div>
<div class=""rp-dpad"">
<button class=""rp-btn rp-up"" onclick=""prevChannel()"" title=""__BTN_PREV__"">▲</button>
<button class=""rp-btn rp-left"" onclick=""prevSource()"" title=""__BTN_SWITCH__"">◀</button>
<button class=""rp-btn rp-ok"" onclick=""togglePlayPause()"" title=""__BTN_PLAY__"">OK</button>
<button class=""rp-btn rp-right"" onclick=""nextSource()"" title=""__BTN_SWITCH__"">▶</button>
<button class=""rp-btn rp-down"" onclick=""nextChannel()"" title=""__BTN_NEXT__"">▼</button>
</div>
<div class=""rp-vol"">
<button class=""rp-vol-btn"" onclick=""volumeStep(-5)"" title=""__BTN_MUTE__"">−</button>
<div class=""rp-vol-track""><div class=""rp-vol-fill"" id=""rpVolFill"" style=""width:70%""></div></div>
<button class=""rp-vol-btn"" onclick=""volumeStep(5)"" title=""__BTN_MUTE__"">+</button>
</div>
<div class=""rp-num-display rp-num-empty"" id=""rpNumDisplay""></div>
<div class=""rp-num-hint"" id=""rpNumHint""></div>
<div class=""rp-numpad"" id=""rpNumpad"">
<button class=""rp-num-btn"" onclick=""numpadPress(1)"">1</button>
<button class=""rp-num-btn"" onclick=""numpadPress(2)"">2</button>
<button class=""rp-num-btn"" onclick=""numpadPress(3)"">3</button>
<button class=""rp-num-btn"" onclick=""numpadPress(4)"">4</button>
<button class=""rp-num-btn"" onclick=""numpadPress(5)"">5</button>
<button class=""rp-num-btn"" onclick=""numpadPress(6)"">6</button>
<button class=""rp-num-btn"" onclick=""numpadPress(7)"">7</button>
<button class=""rp-num-btn"" onclick=""numpadPress(8)"">8</button>
<button class=""rp-num-btn"" onclick=""numpadPress(9)"">9</button>
<button class=""rp-num-btn"" onclick=""numpadClear()"">⌫</button>
<button class=""rp-num-btn"" onclick=""numpadPress(0)"">0</button>
<button class=""rp-num-btn rp-num-ok"" onclick=""numpadCommit()"" title=""__BTN_CONFIRM__"">✓</button>
</div>
<div class=""rp-footer"">
<button class=""rp-text-btn"" onclick=""switchSource()"">__BTN_SWITCH__</button>
<button class=""rp-text-btn"" onclick=""toggleMute()"">__BTN_MUTE__</button>
<button class=""rp-text-btn"" onclick=""fullscreen()"">__BTN_FULLSCREEN__</button>
<button class=""rp-text-btn rp-exit"" onclick=""exitApp()"">__BTN_EXIT__</button>
</div>
</div>

<div class=""now-playing"">
<div class=""now-playing-info"">
<div class=""now-playing-left"">
<div class=""now-playing-channel-row"">
<span class=""now-playing-channel"" id=""channelName"">-</span>
<span class=""now-playing-sep"">|</span>
<span class=""now-playing-program"" id=""programName"">-</span>
<span class=""now-playing-program-time"" id=""programTime""></span>
</div>
</div>
<span class=""now-playing-mode mode-stopped"" id=""statusMode"">__STATUS_STOPPED__</span>
</div>
</div>

<div class=""controls-wrapper"">
<div class=""controls"">
<button class=""btn btn-fullscreen"" onclick=""fullscreen()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z""/></svg><span>__BTN_FULLSCREEN__</span></button>
<button class=""btn btn-play"" onclick=""play()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M8 5v14l11-7z""/></svg><span>__BTN_PLAY__</span></button>
<button class=""btn btn-pause"" onclick=""pause()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M6 19h4V5H6v14zm8-14v14h4V5h-4z""/></svg><span>__BTN_PAUSE__</span></button>
<button class=""btn btn-stop"" onclick=""stop()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M6 6h12v12H6z""/></svg><span>__BTN_STOP__</span></button>
<button class=""btn btn-mute"" onclick=""toggleMute()""><svg class=""icon"" id=""muteIcon"" viewBox=""0 0 24 24""><path d=""M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z""/></svg><span>__BTN_MUTE__</span></button>
</div>
<div class=""controls"">
<button class=""btn btn-nav"" onclick=""prevChannel()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M6 6h2v12H6zm3.5 6l8.5 6V6z""/></svg><span>__BTN_PREV__</span></button>
<button class=""btn btn-nav"" onclick=""nextChannel()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z""/></svg><span>__BTN_NEXT__</span></button>
<button class=""btn btn-switch"" onclick=""switchSource()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46C19.54 15.03 20 13.57 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74C4.46 8.97 4 10.43 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z""/></svg><span>__BTN_SWITCH__</span></button>
<button class=""btn btn-exit"" onclick=""exitApp()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z""/></svg><span>__BTN_EXIT__</span></button>
<button class=""btn"" onclick=""refreshData()""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z""/></svg><span>__BTN_REFRESH__</span></button>
<button class=""btn"" onclick=""seek(-10)""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M11 18V6l-8.5 6 8.5 6zm.5-6l8.5 6V6l-8.5 6z""/></svg><span>-10s</span></button>
<button class=""btn"" onclick=""seek(10)""><svg class=""icon"" viewBox=""0 0 24 24""><path d=""M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z""/></svg><span>+10s</span></button>
<button class=""btn"" id=""btnTimeshift"" onclick=""toggleTimeshift()""><svg class=""icon"" viewBox=""0 0 24 24""><path id=""timeshiftPath"" d=""M8 5v14l11-7z""/></svg><span id=""btnTimeshiftText"">__BTN_TIMESHIFT__</span></button>
</div>
<div class=""ts-scrubber"" id=""tsScrubber"" style=""display:none;"">
<div class=""ts-head""><span id=""tsLabel""></span><span class=""ts-clock"" id=""tsClock""></span></div>
<div class=""ts-meter"" id=""tsMeter"">
<svg class=""icon ts-icon"" viewBox=""0 0 24 24""><path d=""M13 3c-4.97 0-9 4.03-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42C8.27 19.99 10.51 21 13 21c4.97 0 9-4.03 9-9s-4.03-9-9-9zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z""/></svg>
<div class=""ts-track"" id=""tsTrack"" role=""slider"" tabindex=""0"" aria-valuemin=""0"" aria-valuemax=""100"" aria-valuenow=""0"">
<div class=""ts-axis"" id=""tsAxis""></div>
<div class=""ts-fill"" id=""tsFill""></div>
<div class=""ts-knob"" id=""tsKnob""></div>
</div>
<span class=""ts-time"" id=""tsTime""></span>
</div>
<div class=""ts-scale"" id=""tsScale""></div>
<div class=""ts-band"" id=""tsBand""><div class=""ts-band-inner"" id=""tsBandInner""></div></div>
<div class=""ts-detail""><span class=""ts-detail-main"" id=""tsDetailMain""></span><span class=""ts-detail-sub"" id=""tsDetailSub""></span></div>
</div>
</div>

<!-- Reminder Modal -->
<div class=""modal-overlay"" id=""reminderModal"">
<div class=""modal-box"">
<h3 id=""remModalTitle"">__MODAL_ADD_REMINDER__</h3>
<div class=""rem-info""><span id=""remChannel""></span> - <span id=""remProgram""></span></div>
<div class=""rem-info""><span id=""remStart""></span> ~ <span id=""remEnd""></span></div>
<input type=""hidden"" id=""remChannelId"" />
<input type=""hidden"" id=""remStartUtc"" />
<input type=""hidden"" id=""remEndUtc"" />
<input type=""hidden"" id=""remProgramTitle"" />
<label>__LABEL_REMINDER_ACTION__</label>
<select id=""remAction"">
<option value=""play"">__ACTION_PLAY__</option>
<option value=""record_front"">__ACTION_RECORD_FRONT__</option>
<option value=""record_back"">__ACTION_RECORD_BACK__</option>
</select>
<label>__LABEL_PRE_ALERT__</label>
<input type=""number"" id=""remPreAlert"" value=""60"" min=""0"" step=""30"" />
<div class=""mbtns"">
<button class=""mcancel"" onclick=""closeReminderModal()"">__BTN_CANCEL__</button>
<button class=""mok"" onclick=""doAddReminder()"">__BTN_CONFIRM__</button>
</div>
</div>
</div>

<!-- Install to home screen Modal -->
<div class=""modal-overlay"" id=""installModal"">
<div class=""modal-box"">
<h3 id=""installTitle""></h3>
<div class=""rem-info install-body"" id=""installBody""></div>
<div class=""mbtns"">
<button class=""mcancel"" id=""installLater"" onclick=""dismissInstall()""></button>
<button class=""mok"" id=""installGo"" onclick=""doInstall()"" style=""display:none;""></button>
</div>
</div>
</div>

<div class=""volume-control"">
<svg class=""icon volume-icon"" id=""volIcon"" viewBox=""0 0 24 24""><path d=""M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z""/></svg>
<div class=""volume-bar"" id=""volBar"" onclick=""setVolumeFromClick(event)""><div class=""volume-fill"" id=""volFill"" style=""width:70%""></div></div>
<span class=""volume-value"" id=""volValue"">70%</span>
</div>

<div class=""channel-section"">
<div style=""display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;"">
<div class=""section-title"" style=""margin:0;"">__SEC_CHANNELS__ <span id=""currentSourceName"" style=""font-weight:normal;color:#888;font-size:10px;""></span></div>
<button class=""btn"" style=""padding:2px 8px;font-size:10px;"" onclick=""openSourceModal()"">__SEC_SOURCES__</button>
</div>
<div class=""channel-grid"" id=""channelGrid""><div class=""loading"">__LOADING__</div></div>
</div>

<div class=""epg-section"">
<div class=""epg-header"">
<span class=""epg-header-title"">__SEC_EPG__</span>
<div class=""epg-date-nav"">
<button class=""epg-nav-btn"" id=""epgPrevDay"" onclick=""epgPrevDay()"">&#9664;</button>
<span class=""epg-date-display"" id=""epgDateDisplay"" onclick=""showEpgDatePicker()"">--</span>
<button class=""epg-nav-btn"" id=""epgNextDay"" onclick=""epgNextDay()"">&#9654;</button>
<input type=""date"" id=""epgDatePicker"" style=""display:none"" onchange=""loadEpgForDate(this.value)"">
</div>
<span class=""epg-channel-name"" id=""epgChannelName"">-</span>
</div>
<div class=""epg-timeline"">
<span class=""epg-current-time"" id=""currentTime""></span>
</div>
<div class=""epg-list"" id=""epgList""><div class=""loading"">__EPG_SELECT__</div></div>
</div>
</div>

<!-- Source Management Modal -->
<div class=""modal-overlay"" id=""sourceModal"">
<div class=""modal-box"" style=""max-width:420px;"">
<h3 id=""sourceModalTitle"">__SEC_SOURCES__</h3>
<div id=""sourceList""><div class=""loading"">__LOADING__</div></div>
<button class=""btn"" style=""margin-top:8px;width:100%"" onclick=""showAddSourceModal()"">__BTN_ADD_SOURCE__</button>
<div class=""mbtns"" style=""margin-top:12px;"">
<button class=""mcancel"" onclick=""closeSourceModal()"">__BTN_CANCEL__</button>
</div>
</div>
</div>

<!-- Reminder Management -->
<div class=""reminder-section"" id=""reminderSection"" style=""display:none;"">
<div class=""section-title"">__SEC_REMINDERS__</div>
<div id=""reminderList""><div class=""loading"">__LOADING__</div></div>
</div>

<!-- Recording Management -->
<div class=""recording-section"" id=""recordingSection"" style=""display:none;"">
<div class=""section-title"">__SEC_RECORDINGS__</div>
<div id=""recordingList""><div class=""loading"">__LOADING__</div></div>
</div>

<!-- Add Source Modal -->
<div class=""modal-overlay"" id=""addSourceModal"">
<div class=""modal-box"">
<h3>__MODAL_ADD_SOURCE__</h3>
<label>__LABEL_SOURCE_NAME__</label>
<input type=""text"" id=""srcNameInput"" />
<label>__LABEL_SOURCE_URL__</label>
<input type=""text"" id=""srcUrlInput"" />
<div class=""mbtns"">
<button class=""mcancel"" onclick=""closeAddSourceModal()"">__BTN_CANCEL__</button>
<button class=""mok"" onclick=""doAddSource()"">__BTN_CONFIRM__</button>
</div>
</div>
</div>

<!-- Voice control: push-to-talk, hands-free wake word and a typed fallback -->
<div class=""voice-overlay"" id=""voiceOverlay"">
<div class=""voice-box"">
<div class=""voice-head"">
<span id=""voiceTitle""></span>
<button class=""voice-x"" onclick=""closeVoice()"">✕</button>
</div>
<button class=""voice-mic"" id=""voiceMicBtn"" onclick=""voiceMicTap()"">🎙</button>
<div class=""voice-status"" id=""voiceStatus""></div>
<div class=""voice-heard"" id=""voiceHeard""></div>
<div class=""voice-options"" id=""voiceOptions""></div>
<div class=""voice-row"">
<input type=""text"" id=""voiceInput"" onkeydown=""if(event.key==='Enter')submitVoiceInput()"" />
<button id=""voiceSendBtn"" onclick=""submitVoiceInput()""></button>
</div>
<div class=""voice-foot"">
<button id=""voiceHandsFree"" onclick=""toggleHandsFree()""></button>
<span class=""voice-examples"" id=""voiceExamples""></span>
</div>
</div>
</div>

<script>
const T = __T_JSON__;
const HTTPS_PORT = __HTTPS_PORT__;
const SHOW_CHANNELS = __SHOW_CHANNELS__;
const SHOW_EPG = __SHOW_EPG__;
const FAV_GROUP = '__FAV_GROUP__';
const FAV_STAR = '__FAV_STAR__';
// Voice settings are baked in by the server so the page never has to ask for them
const VOICE = __VOICE_JSON__;
let ws; let currentChannelId = ''; let currentVolume = 70; let previousVolume = 70;
let isMuted = false; let channelList = []; let isDarkTheme = true; let isAuthenticated = false;
// Programmes of the channel the EPG section shows: the timeshift scrubber draws its axis from them
let epgPrograms = [];

function applyStaticText() {
document.getElementById('statusMode').textContent = T.status_stopped;
renderThemeToggle();
document.title = T.title;
var rpMode = document.getElementById('rpMode'); if (rpMode) rpMode.textContent = T.status_stopped;
var rpNumHint = document.getElementById('rpNumHint'); if (rpNumHint) rpNumHint.textContent = T.num_hint || '';
renderNumDisplay();
// The scrubber labels come from the string table, so the track needs no placeholder of its own
var tsTrack = document.getElementById('tsTrack');
if (tsTrack) { tsTrack.title = T.ts_drag_hint; tsTrack.setAttribute('aria-label', T.ts_drag_hint); }
var tsLabel = document.getElementById('tsLabel'); if (tsLabel) tsLabel.textContent = T.ts_scrub;
applyRemoteView();
// Sections switched off in the settings stay hidden; their data keeps loading in the background
var chSection = document.querySelector('.channel-section');
if (chSection && !SHOW_CHANNELS) chSection.style.display = 'none';
var epgSection = document.querySelector('.epg-section');
if (epgSection && !SHOW_EPG) epgSection.style.display = 'none';
applyVoiceText();
}

// Language the recogniser should listen in: the configured override, else what the page speaks.
function voiceLang() {
if (VOICE.lang) return VOICE.lang;
var l = (navigator.language || 'zh-CN').toLowerCase();
if (l.indexOf('zh-tw') === 0 || l.indexOf('zh-hk') === 0 || l.indexOf('zh-hant') === 0) return 'zh-TW';
if (l.indexOf('zh') === 0) return 'zh-CN';
if (l.indexOf('ru') === 0) return 'ru-RU';
if (l.indexOf('en') === 0) return 'en-US';
return 'zh-CN';
}

function applyVoiceText() {
var t = function(id, text) { var el = document.getElementById(id); if (el) el.textContent = text; };
t('voiceTitle', T.voice_title);
t('voiceSendBtn', T.voice_send);
var fab = document.getElementById('voiceFab');
if (fab) { fab.title = T.voice_title; fab.style.display = VOICE.enabled ? '' : 'none'; }
var input = document.getElementById('voiceInput');
if (input) input.placeholder = T.voice_input_placeholder;
t('voiceExamples', T.voice_examples);
t('voiceHandsFree', T.voice_handsfree);
var hf = document.getElementById('voiceHandsFree');
if (hf) hf.className = voiceHandsFree ? 'on' : '';
var status = document.getElementById('voiceStatus');
if (status && !voiceBusy) status.textContent = voiceHandsFree ? T.voice_listening : T.voice_hold;
}

function connect() {
const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
ws = new WebSocket(protocol + '//' + location.host);
ws.onopen = function() { loadTheme(); loadRemoteView(); applyStaticText(); loadStatus(); loadChannels(); };
ws.onclose = function() { isAuthenticated = false; setTimeout(connect, 3000); };
ws.onerror = function() { document.getElementById('channelName').textContent = T.connecting; };
ws.onmessage = function(e) {
try {
const data = JSON.parse(e.data);
if (data.type === 'voice') { handleVoiceReply(data); return; }
if (data.requireAuth !== undefined && !data.success && !isAuthenticated) {
document.getElementById('passwordOverlay').style.display = 'flex';
} else if (data.success !== undefined && data.requireAuth !== undefined) {
if (data.success) { isAuthenticated = true; document.getElementById('passwordOverlay').style.display = 'none'; }
else { document.getElementById('authError').style.display = 'block'; }
return;
}
if (data.groups !== undefined) renderChannels(data);
else if (data.programs !== undefined) {
                    // Debug: check badgeHtml in received data
                    if (data.programs && data.programs.length > 0) {
                        console.log('EPG received, first prog badgeHtml:', JSON.stringify(data.programs[0].badgeHtml), 'isCurrent:', data.programs[0].isCurrent);
                        // Find current/live program
                        for (var i = 0; i < data.programs.length; i++) {
                            if (data.programs[i].isCurrent) {
                                console.log('Current program at index', i, ':', data.programs[i].name, 'badgeHtml:', JSON.stringify(data.programs[i].badgeHtml));
                                break;
                            }
                        }
                    }
                    renderEpg(data);
                }
else if (data.channel !== undefined || data.mode !== undefined) updateStatus(data);
else if (data.sources !== undefined) renderSources(data);
else if (data.reminders !== undefined) renderReminders(data);
else if (data.recordings !== undefined) renderRecordings(data);
else if (data.timeshift !== undefined) updateTimeshift(data.timeshift);
} catch (err) { console.error(err); }
};
}

function send(action, data) { if (!data) data = {}; if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ action, ...data })); }
function loadStatus() { send('getStatus'); }
function doAuth() { const pwd = document.getElementById('pwdInput').value; if (!pwd) return; send('auth', { password: pwd }); }
function loadChannels() { send('getChannels'); }
let currentEpgDate = null; // null means default (today)
function loadEpg(channelId, dateStr) { send('getEpg', { channelId: channelId, date: dateStr || null }); }
function loadEpgForDate(dateStr) {
    currentEpgDate = dateStr;
    var today = new Date();
    today.setHours(0,0,0,0);
    var selected = new Date(dateStr);
    selected.setHours(0,0,0,0);
    var diffDays = Math.round((selected - today) / 86400000);
    var label = diffDays === 0 ? '今天' : (diffDays === -1 ? '昨天' : (diffDays === 1 ? '明天' : new Date(dateStr).toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' })));
    document.getElementById('epgDateDisplay').textContent = label;
    document.getElementById('epgDatePicker').style.display = 'none';
    if (currentChannelId) loadEpg(currentChannelId, dateStr);
}
function epgPrevDay() {
    var d = currentEpgDate ? new Date(currentEpgDate) : new Date();
    d.setDate(d.getDate() - 1);
    loadEpgForDate(d.toISOString().split('T')[0]);
}
function epgNextDay() {
    var d = currentEpgDate ? new Date(currentEpgDate) : new Date();
    d.setDate(d.getDate() + 1);
    loadEpgForDate(d.toISOString().split('T')[0]);
}
function showEpgDatePicker() {
    var picker = document.getElementById('epgDatePicker');
    picker.style.display = 'inline';
    picker.value = currentEpgDate || new Date().toISOString().split('T')[0];
    picker.focus();
}
function refreshData() { if (!isAuthenticated) return; loadStatus(); loadChannels(); if (currentChannelId) loadEpg(currentChannelId, currentEpgDate); loadSources(); loadReminders(); loadRecordings(); }

// Source Management
function openSourceModal() { loadSources(); document.getElementById('sourceModal').classList.add('show'); }
function closeSourceModal() { document.getElementById('sourceModal').classList.remove('show'); }
function loadSources() { send('getSources'); }
function renderSources(data) {
  var list = document.getElementById('sourceList');
  if (!list) return;
  list.innerHTML = '';
  if (!data.sources || !data.sources.length) { list.innerHTML = '<div class=""loading"">' + T.source_empty + '</div>'; return; }
  data.sources.forEach(function(s) {
    var div = document.createElement('div');
    div.className = 'source-item' + (s.isSelected ? ' active' : '');
    div.innerHTML = '<div><div class=""sname"">' + s.name + '</div><div class=""surl"">' + s.url + '</div></div>' +
      '<div class=""sbtns"">' +
      (s.isSelected ? '<span class=""sactive"">'+T.source_active+'</span>' : '<button onclick=""doSelectSource(\''+s.name+'\')"">'+T.source_select+'</button>') +
      '<button onclick=""doRemoveSource(\''+s.name+'\')"">'+T.source_remove+'</button></div>';
    list.appendChild(div);
    if (s.isSelected) document.getElementById('currentSourceName').textContent = '(' + s.name + ')';
  });
}
function doSelectSource(name) { send('selectSource', { name }); closeSourceModal(); setTimeout(refreshData, 500); }
function doRemoveSource(name) { if (!confirm(T.confirm_remove_source)) return; send('removeSource', { name }); setTimeout(loadSources, 300); }
function showAddSourceModal() { document.getElementById('addSourceModal').classList.add('show'); }
function closeAddSourceModal() { document.getElementById('addSourceModal').classList.remove('show'); }
function doAddSource() { var n = document.getElementById('srcNameInput').value; var u = document.getElementById('srcUrlInput').value; if (!n || !u) return; send('addSource', { name: n, url: u }); closeAddSourceModal(); setTimeout(loadSources, 500); }

// Reminder Management
function loadReminders() { send('getReminders'); }
function renderReminders(data) {
  var list = document.getElementById('reminderList');
  if (!list) return;
  list.innerHTML = '';
  if (!data.reminders || !data.reminders.length) { list.innerHTML = '<div class=""loading"">' + T.reminder_empty + '</div>'; document.getElementById('reminderSection').style.display = 'block'; return; }
  data.reminders.forEach(function(r) {
    var badge = r.action === 'play' ? 'play' : r.action.includes('record') ? r.action : 'notify';
    var d = new Date(r.startAt);
    var timeStr = d.toLocaleString(document.documentElement.lang, { month:'2-digit', day:'2-digit', hour:'2-digit', minute:'2-digit' });
    var div = document.createElement('div');
    div.className = 'reminder-item';
    div.innerHTML = '<div><div class=""rprogram"">' + r.channelName + ' - ' + r.programTitle + '</div></div>' +
      '<div><span class=""raction ' + badge + '"">' + T['action_' + r.action] + '</span> ' +
      '<span class=""rtime"">' + timeStr + '</span> ' +
      (r.completed ? '<span style=""color:#666"">'+T.status_cancelled+'</span>' : '<button onclick=""doCancelReminder(\''+r.id+'\')"" style=""padding:2px 6px;font-size:10px;background:#ff4757;border:none;border-radius:4px;color:#fff;cursor:pointer"">'+T.reminder_cancel+'</button>') +
      '</div>';
    list.appendChild(div);
  });
  document.getElementById('reminderSection').style.display = 'block';
}
function doCancelReminder(id) { send('cancelReminder', { id }); setTimeout(loadReminders, 300); }

// Recording Management
function loadRecordings() { send('getRecordings'); }
function renderRecordings(data) {
  var list = document.getElementById('recordingList');
  if (!list) return;
  list.innerHTML = '';
  if (!data.recordings || !data.recordings.length) { list.innerHTML = '<div class=""loading"">' + T.recording_empty + '</div>'; document.getElementById('recordingSection').style.display = 'block'; return; }
  data.recordings.forEach(function(r) {
    var div = document.createElement('div');
    div.className = 'recording-item';
    div.innerHTML = '<div><div class=""rprogram"">' + r.channelName + ' - ' + r.programTitle + '</div><div class=""rcstatus"">' + r.type + ' | ' + r.statusLabel + '</div></div>' +
      '<div><span class=""rcsize"">' + r.sizeLabel + '</span> ' +
      (r.status === 'Recording' || r.status === 'Waiting' ? '<button onclick=""doStopRecording(\''+r.id+'\')"" style=""padding:2px 6px;font-size:10px;background:#ff4757;border:none;border-radius:4px;color:#fff;cursor:pointer"">'+T.recording_stop+'</button>' : '') +
      (r.status === 'Completed' || r.status === 'Failed' || r.status === 'Cancelled' || r.status === 'Stopped' ? '<button onclick=""doDeleteRecording(\''+r.id+'\')"" style=""padding:2px 6px;font-size:10px;background:#57606f;border:none;border-radius:4px;color:#fff;cursor:pointer"">'+T.recording_delete+'</button>' : '') +
      '</div>';
    list.appendChild(div);
  });
  document.getElementById('recordingSection').style.display = 'block';
}
function doStopRecording(id) { send('stopRecording', { id }); setTimeout(loadRecordings, 300); }
function doDeleteRecording(id) { send('deleteRecording', { id }); setTimeout(loadRecordings, 300); }

// Speed & Seek
async function seek(seconds) { send('seek', { seconds }); await loadStatus(); }
async function setSpeed(speed) { send('setSpeed', { speed }); await loadStatus(); }

// Timeshift toggle
var isTimeshiftEnabled = false;
function toggleTimeshift() { send('setTimeshift', { enabled: !isTimeshiftEnabled }); setTimeout(loadStatus, 400); }
function updateTimeshiftBtn() {
  var btn = document.getElementById('btnTimeshift');
  var path = document.getElementById('timeshiftPath');
  if (btn) { btn.className = 'btn' + (isTimeshiftEnabled ? ' active' : ''); }
  if (path) {
    // Play icon (triangle) when off, Pause icon (two bars) when on
    path.setAttribute('d', isTimeshiftEnabled ? 'M6 19h4V5H6v14zm8-14v14h4V5h-4z' : 'M8 5v14l11-7z');
  }
  // One label for both states: the pressed-in face plus the pause glyph already mark the latched
  // state, so the text must not grow a second glyph and the button keeps its plain colour.
  var text = document.getElementById('btnTimeshiftText');
  if (text) text.textContent = T.btn_timeshift;
  if (btn) btn.title = isTimeshiftEnabled ? T.btn_timeshift_on : T.btn_timeshift_off;
}

// --- Timeshift scrubber -----------------------------------------------------
// The window is whatever the server last reported: the full status after any action and, while the
// scrubber is open, the small getTimeshift reply every second. A drag only repaints the track; the
// seek is sent once on release as the offset from the window start, so dragging cannot hammer the
// player with one load per pixel. The server clamps that offset into the live window.
let tsWindow = { active: false, from: 0, to: 0, position: 0 };
let tsDragging = false, tsDragSeconds = 0, tsTicker = null;

function tsTotalSeconds() { return Math.max(0, (tsWindow.to - tsWindow.from) / 1000); }
function tsDateText(ms) { var d = new Date(ms); return (d.getMonth() + 1) + '-' + (d.getDate() < 10 ? '0' : '') + d.getDate(); }
function tsClockText(ms, withDate) {
  var d = new Date(ms), h = d.getHours(), m = d.getMinutes();
  var hm = (h < 10 ? '0' : '') + h + ':' + (m < 10 ? '0' : '') + m;
  if (!withDate) return hm;
  return tsDateText(ms) + ' ' + hm;
}
// Five ticks under the bar. A window of hours is labelled with clock times, a window of days with
// dates, so the labels stay readable however long the seekable window happens to be.
var tsScaleKey = null;
function renderTimeshiftScale() {
  var scale = document.getElementById('tsScale');
  if (!scale) return;
  var key = tsWindow.active ? tsWindow.from + '|' + tsWindow.to : '';
  if (key === tsScaleKey) return;
  tsScaleKey = key;
  scale.innerHTML = '';
  if (!tsWindow.active || tsWindow.to <= tsWindow.from) return;
  var span = tsWindow.to - tsWindow.from;
  var byDate = span >= 36 * 3600 * 1000;
  for (var i = 0; i < 5; i++) {
    var at = tsWindow.from + span * i / 4;
    var tick = document.createElement('span');
    tick.textContent = byDate ? tsDateText(at) : tsClockText(at, false);
    scale.appendChild(tick);
  }
}
function tsProgramAt(ms) {
  var found = null;
  epgPrograms.forEach(function(p) {
    var s = p.startISO ? new Date(p.startISO).getTime() : 0;
    var e = p.endISO ? new Date(p.endISO).getTime() : 0;
    if (!s || !e) return;
    if (ms >= s && ms < e) found = p;
  });
  return found;
}
// One segment per programme that overlaps the window, labelled with its start time: that is the EPG
// time axis the user drags along.
function renderTimeshiftAxis() {
  var axis = document.getElementById('tsAxis');
  if (!axis) return;
  axis.innerHTML = '';
  if (!tsWindow.active || tsWindow.to <= tsWindow.from) return;
  var from = tsWindow.from, to = tsWindow.to;
  epgPrograms.forEach(function(p) {
    var s = p.startISO ? new Date(p.startISO).getTime() : 0;
    var e = p.endISO ? new Date(p.endISO).getTime() : 0;
    if (!s || !e || e <= from || s >= to) return;
    var start = Math.max(s, from), end = Math.min(e, to);
    var seg = document.createElement('div');
    seg.className = p.isCurrent ? 'ts-seg current' : 'ts-seg';
    seg.style.left = ((start - from) / (to - from) * 100) + '%';
    seg.style.width = ((end - start) / (to - from) * 100) + '%';
    seg.title = (p.name || '') + ' ' + (p.start || '') + ' - ' + (p.end || '');
    axis.appendChild(seg);
  });
}
// The EPG band under the track: one block per programme that overlaps the window (plus half an hour
// either side so the neighbours are visible). Blocks grow with their slot length so the band reads
// like a TV EPG bar, and the block under the cursor is highlighted and centred without scrolling the
// page. Rebuilt only when the window or the programme list changes.
var tsBandSignature = null, tsBlockSpans = [], tsCentredBlock = null;
function renderTimeshiftBand() {
  var inner = document.getElementById('tsBandInner');
  if (!inner) return;
  if (!tsWindow.active || tsWindow.to <= tsWindow.from) { inner.innerHTML = ''; tsBandSignature = null; tsBlockSpans = []; tsCentredBlock = null; return; }
  var pad = 30 * 60 * 1000;
  var list = epgPrograms.filter(function(p) {
    var s = p.startISO ? new Date(p.startISO).getTime() : 0;
    var e = p.endISO ? new Date(p.endISO).getTime() : 0;
    return !!s && !!e && e > tsWindow.from - pad && s < tsWindow.to + pad;
  });
  var signature = tsWindow.from + '|' + tsWindow.to + '|' + list.map(function(p) { return p.startISO; }).join(',');
  if (signature === tsBandSignature) return;
  tsBandSignature = signature;
  tsCentredBlock = null;
  tsBlockSpans = [];
  inner.innerHTML = '';
  var playhead = document.createElement('div');
  playhead.className = 'ts-playhead';
  playhead.id = 'tsPlayhead';
  if (!list.length) {
    var empty = document.createElement('div');
    empty.className = 'ts-block';
    var emptyName = document.createElement('div');
    emptyName.className = 'ts-block-name';
    emptyName.textContent = T.ts_program_unknown || '';
    empty.appendChild(emptyName);
    inner.appendChild(empty);
    inner.appendChild(playhead);
    return;
  }
  var prevDay = null;
  list.forEach(function(p) {
    var s = new Date(p.startISO).getTime(), e = new Date(p.endISO).getTime();
    // The band can cross midnight, so a block carries the date when its day differs from the one
    // before it (and on the first block), exactly like a TV EPG bar.
    var day = new Date(s).toDateString();
    var showDate = prevDay === null || day !== prevDay;
    prevDay = day;
    var block = document.createElement('button');
    block.type = 'button';
    block.className = 'ts-block';
    block.style.flexGrow = String(Math.max(1, Math.round((e - s) / 60000)));
    block.setAttribute('data-start', String(s));
    block.setAttribute('data-end', String(e));
    var time = document.createElement('div');
    time.className = 'ts-block-time';
    time.textContent = tsClockText(s, showDate);
    var name = document.createElement('div');
    name.className = 'ts-block-name';
    name.textContent = p.name || '';
    block.appendChild(time);
    block.appendChild(name);
    inner.appendChild(block);
    tsBlockSpans.push({ el: block, start: s, end: e });
  });
  inner.appendChild(playhead);
}
// Swiping the band is the user browsing the timeline, so the automatic centring stays off for five
// seconds after the last touch and then snaps back to the slot the timeshift cursor is sitting in.
var tsHoldUntil = 0, tsRecentreTimer = null;
function tsHoldBand() {
  tsHoldUntil = Date.now() + 5000;
  if (tsRecentreTimer) clearTimeout(tsRecentreTimer);
  tsRecentreTimer = setTimeout(function() {
    tsRecentreTimer = null;
    tsHoldUntil = 0;
    tsCentredBlock = null;
    if (tsWindow.active) positionPlayhead(tsWindow.from + (tsDragging ? tsDragSeconds : tsWindow.position) * 1000);
  }, 5100);
}
function tsReleaseBand() {
  tsHoldUntil = 0;
  if (tsRecentreTimer) { clearTimeout(tsRecentreTimer); tsRecentreTimer = null; }
}
// Move the playhead to the cursor and mark the slot it falls in, centring that slot in the band.
function positionPlayhead(ms) {
  var head = document.getElementById('tsPlayhead');
  if (!head || !tsBlockSpans.length) return;
  var inner = document.getElementById('tsBandInner');
  var innerRect = inner.getBoundingClientRect();
  var x = null, current = null;
  for (var i = 0; i < tsBlockSpans.length; i++) {
    var span = tsBlockSpans[i];
    var hit = ms >= span.start && ms < span.end;
    span.el.classList.toggle('current', hit);
    if (!hit) continue;
    var rect = span.el.getBoundingClientRect();
    x = (rect.left - innerRect.left) + (ms - span.start) / (span.end - span.start) * rect.width;
    current = { span: span, rect: rect };
  }
  if (x === null || !current) return;
  head.style.left = x + 'px';
  var band = document.getElementById('tsBand');
  if (!band || band.clientWidth === 0 || band.scrollWidth <= band.clientWidth) return;
  if (Date.now() < tsHoldUntil) return;
  var key = String(current.span.start);
  if (key === tsCentredBlock) return;
  tsCentredBlock = key;
  var bandRect = band.getBoundingClientRect();
  var target = band.scrollLeft + (current.rect.left - bandRect.left) - (band.clientWidth - current.rect.width) / 2;
  band.scrollLeft = Math.max(0, Math.min(target, band.scrollWidth - band.clientWidth));
}
function paintTimeshift(seconds) {
  var total = tsTotalSeconds();
  var pct = total > 0 ? Math.max(0, Math.min(100, seconds / total * 100)) : 0;
  var fill = document.getElementById('tsFill'); if (fill) fill.style.width = pct + '%';
  var knob = document.getElementById('tsKnob'); if (knob) knob.style.left = pct + '%';
  var track = document.getElementById('tsTrack'); if (track) track.setAttribute('aria-valuenow', String(Math.round(pct)));
  var atMs = tsWindow.from + seconds * 1000;
  var clock = document.getElementById('tsClock');
  if (clock) clock.textContent = tsClockText(atMs, true);
  // The cell beside the bar is the clock column of the row, mirroring the volume percentage.
  var time = document.getElementById('tsTime');
  if (time) time.textContent = tsClockText(atMs, false);
  renderTimeshiftScale();
  // Tie the position to the EPG: name and slot of the programme that runs at the cursor. The card
  // under the header repeats it, because while timeshift runs the live programme is not what the
  // user is watching any more.
  var prog = tsProgramAt(atMs);
  var s = prog && prog.startISO ? new Date(prog.startISO).getTime() : 0;
  var e = prog && prog.endISO ? new Date(prog.endISO).getTime() : 0;
  var name = (prog && prog.name) ? prog.name : (T.ts_program_unknown || '');
  var main = document.getElementById('tsDetailMain'); if (main) main.textContent = name;
  var sub = document.getElementById('tsDetailSub');
  if (sub) sub.textContent = (s && e) ? tsClockText(s, true) + ' - ' + tsClockText(e, true) : tsClockText(atMs, true);
  var cardName = document.getElementById('programName'); if (cardName) cardName.textContent = name;
  var cardTime = document.getElementById('programTime');
  if (cardTime) cardTime.textContent = (s && e) ? tsClockText(s, false) + '-' + tsClockText(e, false) : '';
  positionPlayhead(atMs);
}
function updateTimeshift(t) {
  var box = document.getElementById('tsScrubber');
  var usable = !!(t && t.active && t.fromMs && t.toMs && t.toMs > t.fromMs);
  tsWindow.active = usable;
  if (!usable) {
    tsWindow.from = 0; tsWindow.to = 0; tsWindow.position = 0;
    if (box) box.style.display = 'none';
    stopTimeshiftTicker();
    return;
  }
  tsWindow.from = t.fromMs; tsWindow.to = t.toMs;
  tsWindow.position = Math.max(0, Math.min(tsTotalSeconds(), t.positionSec || 0));
  if (box) box.style.display = 'block';
  renderTimeshiftAxis();
  renderTimeshiftBand();
  paintTimeshift(tsDragging ? tsDragSeconds : tsWindow.position);
  startTimeshiftTicker();
}
function startTimeshiftTicker() {
  if (tsTicker) return;
  tsTicker = setInterval(function() {
    if (tsDragging || document.hidden) return;
    send('getTimeshift');
  }, 1000);
}
function stopTimeshiftTicker() { if (tsTicker) { clearInterval(tsTicker); tsTicker = null; } }
function tsSecondsFromEvent(ev) {
  var track = document.getElementById('tsTrack');
  if (!track) return 0;
  var rect = track.getBoundingClientRect();
  var clientX = (ev.clientX === undefined && ev.touches && ev.touches.length) ? ev.touches[0].clientX : ev.clientX;
  var ratio = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
  return Math.max(0, Math.min(1, ratio)) * tsTotalSeconds();
}
(function bindTimeshiftScrubber() {
  var track = document.getElementById('tsTrack');
  if (!track) return;
  // The pointer handlers hang off the whole row, because the bar itself is only as tall as the volume
  // bar; the maths still measures the bar, so a grab just above or below it lands on the right spot.
  var meter = document.getElementById('tsMeter') || track;
  function seekToDrag() {
    if (!tsDragging) return;
    tsDragging = false;
    send('seekTimeshift', { seconds: Math.round(tsDragSeconds) });
    setTimeout(loadStatus, 400);
  }
  meter.addEventListener('pointerdown', function(ev) {
    if (!tsWindow.active) return;
    tsDragging = true;
    tsDragSeconds = tsSecondsFromEvent(ev);
    paintTimeshift(tsDragSeconds);
    if (meter.setPointerCapture) { try { meter.setPointerCapture(ev.pointerId); } catch (err) { } }
    ev.preventDefault();
  });
  meter.addEventListener('pointermove', function(ev) {
    if (!tsDragging) return;
    tsDragSeconds = tsSecondsFromEvent(ev);
    paintTimeshift(tsDragSeconds);
    ev.preventDefault();
  });
  meter.addEventListener('pointerup', seekToDrag);
  meter.addEventListener('pointercancel', seekToDrag);
  // Arrow keys step ten seconds, matching the -10s / +10s buttons beside the track
  meter.addEventListener('keydown', function(ev) {
    if (!tsWindow.active) return;
    var base = tsWindow.position;
    if (ev.key === 'ArrowLeft') { tsDragSeconds = Math.max(0, base - 10); tsDragging = true; seekToDrag(); }
    else if (ev.key === 'ArrowRight') { tsDragSeconds = Math.min(tsTotalSeconds(), base + 10); tsDragging = true; seekToDrag(); }
    else return;
    ev.preventDefault();
  });
  // Tapping a programme card seeks to that programme's start, which is the EPG link the user asked
  // for: the card names the slot, so picking it jumps the timeshift cursor straight there instead of
  // hunting for the right spot on the track.
  var strip = document.getElementById('tsBand');
  if (strip) {
    // Any touch on the band counts as the user browsing it, which pauses the automatic centring.
    strip.addEventListener('pointerdown', tsHoldBand, { passive: true });
    strip.addEventListener('touchstart', tsHoldBand, { passive: true });
    strip.addEventListener('wheel', tsHoldBand, { passive: true });
    strip.addEventListener('click', function(ev) {
      if (!tsWindow.active) return;
      var card = ev.target && ev.target.closest ? ev.target.closest('.ts-block[data-start]') : null;
      if (!card) return;
      var startMs = Number(card.getAttribute('data-start'));
      var seconds = Math.max(0, Math.min(tsTotalSeconds(), (startMs - tsWindow.from) / 1000));
      send('seekTimeshift', { seconds: Math.round(seconds) });
      tsDragSeconds = seconds;
      // Picking a slot is an explicit choice, so the band follows it straight away.
      tsReleaseBand();
      tsCentredBlock = null;
      paintTimeshift(seconds);
      setTimeout(loadStatus, 400);
    });
  }
})();

// EPG replay/timeshift/remind
async function doReplayProgram(channelId, programTitle, start, end) { send('replayProgram', { channelId: channelId, programTitle: programTitle, start: start, end: end }); await loadStatus(); }
function showAddReminderModal(channelId, channelName, programTitle, start, end, action) {
  var modal = document.getElementById('reminderModal');
  if (!modal) return;
  document.getElementById('remChannel').textContent = channelName;
  document.getElementById('remProgram').textContent = programTitle;
  document.getElementById('remStart').textContent = new Date(start).toLocaleString();
  document.getElementById('remEnd').textContent = new Date(end).toLocaleString();
  document.getElementById('remChannelId').value = channelId;
  document.getElementById('remStartUtc').value = start;
  document.getElementById('remEndUtc').value = end;
  document.getElementById('remProgramTitle').value = programTitle;
  document.getElementById('remAction').value = action || 'play';
  modal.classList.add('show');
}
function closeReminderModal() { var modal = document.getElementById('reminderModal'); if (modal) modal.classList.remove('show'); }
async function doAddReminder() {
  var channelId = document.getElementById('remChannelId').value;
  var startAt = document.getElementById('remStartUtc').value;
  var endTime = document.getElementById('remEndUtc').value;
  var programTitle = document.getElementById('remProgramTitle').value;
  var action = document.getElementById('remAction').value;
  var preAlert = parseInt(document.getElementById('remPreAlert').value) || 60;
  if (!channelId || !startAt) return;
  send('addReminder', { channelId: channelId, startAt: startAt, endTime: endTime, action: action, programTitle: programTitle, preAlertSeconds: preAlert });
  closeReminderModal();
}

// The theme button is icon-only in the header: the glyph carries the state and the full
// label stays on title/aria for screen readers.
function renderThemeToggle() {
var btn = document.getElementById('themeToggle'); if (!btn) return;
var label = isDarkTheme ? (T.theme_dark || '') : (T.theme_light || '');
btn.textContent = isDarkTheme ? '🌙' : '☀️';
btn.title = label; btn.setAttribute('aria-label', label);
}
function toggleTheme() {
isDarkTheme = !isDarkTheme;
document.body.classList.toggle('dark-theme', isDarkTheme);
renderThemeToggle();
try { localStorage.setItem('remote-theme', isDarkTheme ? 'dark' : 'light'); } catch (e) { }
updateThemeColorMeta();
}
function loadTheme() {
var saved = null; try { saved = localStorage.getItem('remote-theme'); } catch (e) { }
isDarkTheme = saved === 'dark';
document.body.classList.toggle('dark-theme', isDarkTheme);
renderThemeToggle();
updateThemeColorMeta();
}
function updateThemeColorMeta() {
var meta = document.querySelector('meta[name=""theme-color""]');
if (meta) meta.setAttribute('content', isDarkTheme ? '#0a0a0a' : '#d9dee5');
}

function updateVolumeUI() {
document.getElementById('volFill').style.width = (isMuted ? 0 : currentVolume) + '%';
document.getElementById('volValue').textContent = isMuted ? 'x' : currentVolume + '%';
var rpVolFill = document.getElementById('rpVolFill'); if (rpVolFill) rpVolFill.style.width = (isMuted ? 0 : currentVolume) + '%';
var volPath = isMuted
? 'M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z'
: 'M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z';
var volIconEl = document.getElementById('volIcon'); var muteIconEl = document.getElementById('muteIcon');
if (volIconEl && volIconEl.querySelector('path')) volIconEl.querySelector('path').setAttribute('d', volPath);
if (muteIconEl && muteIconEl.querySelector('path')) muteIconEl.querySelector('path').setAttribute('d', volPath);
}

function updateStatus(s) {
var modeMap = { Live: T.mode_live, Replay: T.mode_replay, Timeshift: T.mode_timeshift, Recording: T.mode_recording, LocalFile: T.mode_local, Stopped: T.mode_default };
var modeClass = { Live:'mode-live', Replay:'mode-replay', Timeshift:'mode-timeshift', Recording:'mode-recording', LocalFile:'mode-local', Stopped:'mode-stopped' };
var el = document.getElementById('statusMode');
el.textContent = modeMap[s.mode] || s.mode || T.mode_default;
el.className = 'now-playing-mode ' + (modeClass[s.mode] || 'mode-stopped');
document.getElementById('channelName').textContent = (s.channel && s.channel.name) || T.status_stopped;
document.getElementById('programName').textContent = (s.currentProgram && s.currentProgram.name) || '';
document.getElementById('programTime').textContent = s.currentProgram ? (s.currentProgram.date + ' ' + s.currentProgram.start + ' - ' + s.currentProgram.end) : '';
var rpChannel = document.getElementById('rpChannel'); if (rpChannel) rpChannel.textContent = (s.channel && s.channel.name) || T.status_stopped;
var rpProgram = document.getElementById('rpProgram'); if (rpProgram) rpProgram.textContent = (s.currentProgram && s.currentProgram.name) || '';
var rpModeEl = document.getElementById('rpMode');
if (rpModeEl) { rpModeEl.textContent = modeMap[s.mode] || s.mode || T.mode_default; rpModeEl.className = 'rp-mode ' + (modeClass[s.mode] || 'mode-stopped'); }
isPlayingState = s.mode !== 'Stopped' && !!s.mode;
currentChannelId = (s.channel && s.channel.id) || '';
if (!s.muted && s.volume > 0) previousVolume = s.volume;
currentVolume = s.muted ? previousVolume : (s.volume || 0);
isMuted = s.muted || false;
isTimeshiftEnabled = s.timeshiftEnabled || false;
updateTimeshiftBtn();
// The scrubber belongs to the full view: it appears as soon as the status reports a window
updateTimeshift(s.timeshift);
updateVolumeUI(); updateChannelActive();
if (currentChannelId) loadEpg(currentChannelId, currentEpgDate);
// Update date display to today if no date selected
if (!currentEpgDate) {
    document.getElementById('epgDateDisplay').textContent = '今天';
}
document.getElementById('currentTime').textContent = new Date().toLocaleTimeString(document.documentElement.lang, { hour: '2-digit', minute: '2-digit' });
}

function renderChannels(data) {
var grid = document.getElementById('channelGrid'); grid.innerHTML = ''; channelList = [];
channelNumbers = {}; numberToId = {};
// The server numbers every channel in playlist group order: the numbers ignore group
// boundaries and are not shifted by the favourites section, so a channel keeps the
// same number wherever it is listed (the favourites section reuses it). Reading that
// number keeps the page and the player overlay showing the same value.
function assignNumber(c) {
if (!c || !c.id) return;
var n = c.number || 0;
if (n > 0) { channelNumbers[c.id] = n; numberToId[n] = c.id; }
}
function isFavGroup(g) { return !!(g && (g.isFavorites || g.name === FAV_GROUP)); }
if (data.groups && data.groups.length) { data.groups.forEach(function(g) { if (isFavGroup(g)) return; if (g.channels) g.channels.forEach(assignNumber); }); }
if (data.favorites && data.favorites.length) { data.favorites.forEach(assignNumber); }
if (data.favorites && data.favorites.length) { data.favorites.forEach(function(c) { channelList.push(c); addChannelItem(grid, c, true); }); }
if (data.groups && data.groups.length) { data.groups.forEach(function(g) { if (isFavGroup(g)) return; if (g.channels) g.channels.forEach(function(c) { channelList.push(c); addChannelItem(grid, c); }); }); }
updateChannelActive();
}
function addChannelItem(grid, c, isFavorite) {
var div = document.createElement('div'); div.className = 'channel-item'; div.dataset.id = c.id;
var logoContainer = document.createElement('div'); logoContainer.className = 'logo';
if (c.logo && c.logo.length > 0) {
var imgStyle = 'height:56px;width:auto;max-width:80px;object-fit:contain';
var fallbackIcon = '<span style=font-size:22px;display:none;align-items:center;justify-content:center;width:80px;height:56px>' + (isFavorite ? FAV_STAR : 'TV') + '</span>';
if (c.logo.indexOf('http') === 0) {
// External HTTP URL - use directly
var img = document.createElement('img'); img.src = c.logo; img.style = imgStyle; img.crossOrigin = 'anonymous';
img.onerror = function() { img.style.display = 'none'; img.nextSibling.style.display = 'flex'; };
logoContainer.innerHTML = fallbackIcon; logoContainer.insertBefore(img, logoContainer.firstChild);
} else {
// Local file path - convert to /logo/ endpoint
var localPath = c.logo.replace('file:///', '').replace(/\//g, '\\\\');
var encodedPath = encodeURIComponent(localPath).replace(/%5C/g, '/');
var img = document.createElement('img'); img.src = '/logo/' + encodedPath; img.style = imgStyle;
img.onerror = function() { img.style.display = 'none'; img.nextSibling.style.display = 'flex'; };
logoContainer.innerHTML = fallbackIcon; logoContainer.insertBefore(img, logoContainer.firstChild);
}
} else {
logoContainer.innerHTML = '<span style=font-size:22px;display:flex;align-items:center;justify-content:center;width:80px;height:56px>' + (isFavorite ? FAV_STAR : 'TV') + '</span>';
}
var chNumber = channelNumbers[c.id];
var numHtml = chNumber ? '<span class=ch-number>' + chNumber + '</span>' : '';
var nameHtml = '<div class=name>' + numHtml + (c.name || '') + '</div>';
var progHtml = '<div class=program>' + (c.currentProgram || '') + '</div>';
var timeHtml = '<div class=time>' + (c.currentTime || '') + '</div>';
var infoDiv = document.createElement('div'); infoDiv.className = 'info'; infoDiv.innerHTML = nameHtml + progHtml + timeHtml;
div.appendChild(logoContainer); div.appendChild(infoDiv);
div.onclick = function() { changeChannel(c.id); };
grid.appendChild(div);
}
function renderEpg(data) {
var list = document.getElementById('epgList'); var channelName = document.getElementById('epgChannelName');
epgPrograms = data.programs || [];
renderTimeshiftAxis();
renderTimeshiftBand();
if (!data.programs || !data.programs.length) { list.innerHTML = '<div class=""loading"">' + T.epg_empty + '</div>'; return; }
var channel = channelList.find(function(c) { return c.id === data.channelId; });
channelName.textContent = channel ? channel.name : T.status_stopped;
list.innerHTML = '';
var now = new Date();
data.programs.forEach(function(p, i) {
var div = document.createElement('div');
div.className = 'epg-item' + (p.isCurrent ? ' current' : '');
// Use ISO UTC timestamps for accurate time comparison
var pStart = p.startISO ? new Date(p.startISO) : null;
var pEnd = p.endISO ? new Date(p.endISO) : null;
var isPast = pEnd && pEnd < now;
var isFuture = pStart && pStart > now;
if (isPast) div.classList.add('past');
// Add badge-based class for colored bottom border (LIVE=red, replay=green, reminder=blue)
if (p.badge === 'live') div.classList.add('epg-item-live');
else if (p.badge === 'replay') div.classList.add('epg-item-replay');
else if (p.badge === 'reminder') div.classList.add('epg-item-reminder');
var actionHtml = '';
if (p.isCurrent) {
actionHtml += '<button class=""epg-btn-live"" onclick=""event.stopPropagation();changeChannel(\'' + data.channelId + '\')"">' + T.btn_watch + '</button>';
}
else if (isPast && p.canReplay) {
actionHtml += '<button class=""epg-btn-replay"" onclick=""event.stopPropagation();doReplayProgram(\'' + data.channelId + '\', \'' + p.name + '\', \'' + p.startISO + '\', \'' + p.endISO + '\')"">' + T.btn_replay + '</button>';
}
else if (isFuture) {
actionHtml += '<button class=""epg-btn-remind"" onclick=""event.stopPropagation();showAddReminderModal(\'' + data.channelId + '\', \'' + (channel ? channel.name : '') + '\', \'' + p.name + '\', \'' + p.startISO + '\', \'' + p.endISO + '\', \'play\')"">' + T.btn_remind + '</button>';
}
div.innerHTML = '<div class=""epg-time""><span>' + (p.date ? p.date + ' ' : '') + p.start + '</span><span class=""epg-time-end"">' + p.end + '</span></div>' +
'<div class=""epg-content"">' + p.name + '</div>' +
'<div class=""epg-right-col"">' + actionHtml + '</div>';
div.onclick = function() { div.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); changeChannel(data.channelId); };
list.appendChild(div);
});
locateInList(list, list.querySelector('.epg-item.current'));
}
// Scroll a list so the given item sits inside its own scrollbar. The page scrollbar must stay where
// it is: opening or refreshing the page may not jump the whole page around.
function locateInList(list, item) {
if (!list || !item || list.scrollHeight <= list.clientHeight) return;
var listRect = list.getBoundingClientRect(); var itemRect = item.getBoundingClientRect();
var target = list.scrollTop + (itemRect.top - listRect.top) - (list.clientHeight - itemRect.height) / 2;
list.scrollTop = Math.max(0, Math.min(target, list.scrollHeight - list.clientHeight));
}
function updateChannelActive() {
var items = document.querySelectorAll('.channel-item');
var active = null;
for (var i = 0; i < items.length; i++) {
var el = items[i];
if (el.dataset.id === currentChannelId) { el.classList.add('active'); active = el; } else el.classList.remove('active');
}
locateInList(document.getElementById('channelGrid'), active);
}
async function play() { send('play'); await loadStatus(); }
async function pause() { send('pause'); await loadStatus(); }
async function stop() { send('stop'); await loadStatus(); }
async function toggleMute() { send('volume', { volume: isMuted ? previousVolume : 0 }); await loadStatus(); }
async function setVolumeFromClick(e) {
var bar = document.getElementById('volBar');
var pct = Math.round((e.clientX - bar.getBoundingClientRect().left) / bar.offsetWidth * 100);
previousVolume = pct; send('volume', { volume: pct }); await loadStatus();
}
async function changeChannel(id) { send('channel', { channelId: id }); await loadStatus(); }
async function prevChannel() { var idx = channelList.findIndex(function(c) { return c.id === currentChannelId; }); if (idx > 0) await changeChannel(channelList[idx - 1].id); }
async function nextChannel() { var idx = channelList.findIndex(function(c) { return c.id === currentChannelId; }); if (idx >= 0 && idx < channelList.length - 1) await changeChannel(channelList[idx + 1].id); }
function fullscreen() { send('fullscreen'); }
function switchSource() { send('switchSource'); }
// ---- Standard remote view (D-pad / volume / number keys) ----
let remoteMode = false;
let isPlayingState = false;
let channelNumbers = {};
let numberToId = {};
let numBuffer = '';
let numTimer = null;

function loadRemoteView() {
try { remoteMode = localStorage.getItem('srcbox_remote_view') === 'remote'; } catch (e) { remoteMode = false; }
applyRemoteView();
}
function toggleRemoteView() {
remoteMode = !remoteMode;
try { localStorage.setItem('srcbox_remote_view', remoteMode ? 'remote' : 'full'); } catch (e) { }
applyRemoteView();
}
function applyRemoteView() {
document.body.classList.toggle('remote-mode', remoteMode);
var btn = document.getElementById('viewToggle');
if (btn) btn.textContent = remoteMode ? (btn.dataset.full || 'Full') : (btn.dataset.standard || 'Remote');
var rpVolFill = document.getElementById('rpVolFill');
if (rpVolFill) rpVolFill.style.width = (isMuted ? 0 : currentVolume) + '%';
// The lists may have been filled while the section was hidden: locate the current entries now that
// they are visible again. Reading the layout after the class toggle already reflects the new view.
locateInList(document.getElementById('channelGrid'), document.querySelector('.channel-item.active'));
locateInList(document.getElementById('epgList'), document.querySelector('.epg-item.current'));
// Same for the timeshift strip: it may have been rendered while the full view was hidden, when the
// strip had no measurable width and therefore could not be centred on the cursor's programme.
if (tsWindow.active) { tsCentredBlock = null; renderTimeshiftBand(); paintTimeshift(tsDragging ? tsDragSeconds : tsWindow.position); }
}
async function togglePlayPause() { if (isPlayingState) { await pause(); } else { await play(); } }
async function volumeStep(delta) {
var next = Math.round(currentVolume) + delta;
if (next < 0) next = 0;
if (next > 100) next = 100;
if (next > 0) previousVolume = next;
send('volume', { volume: next }); await loadStatus();
}
function prevSource() { send('prevSource'); }
function nextSource() { send('nextSource'); }
function renderNumDisplay() {
var el = document.getElementById('rpNumDisplay'); if (!el) return;
if (numBuffer) { el.textContent = numBuffer; el.classList.remove('rp-num-empty'); }
else { el.textContent = T.num_placeholder || ''; el.classList.add('rp-num-empty'); }
}
function showNumHint(msg) {
var el = document.getElementById('rpNumHint'); if (!el) return;
el.textContent = msg;
setTimeout(function () { if (el.textContent === msg) el.textContent = ''; }, 2000);
}
function numpadPress(d) {
numBuffer += String(d);
if (numBuffer.length > 3) numBuffer = numBuffer.slice(-3);
renderNumDisplay();
if (numTimer) clearTimeout(numTimer);
numTimer = setTimeout(numpadCommit, 1200);
}
function numpadClear() {
numBuffer = '';
if (numTimer) { clearTimeout(numTimer); numTimer = null; }
renderNumDisplay();
}
function numpadCommit() {
if (numTimer) { clearTimeout(numTimer); numTimer = null; }
if (!numBuffer) return;
var target = parseInt(numBuffer, 10);
numBuffer = '';
renderNumDisplay();
var id = numberToId[target];
if (id) { changeChannel(id); } else { showNumHint(T.channel_not_found || 'Not found'); }
}
async function exitApp() { if (confirm(T.exit_confirm)) send('exit'); }

// Install to home screen: native prompt when the browser offers one, Safari instructions for iOS,
// and a certificate hint when the page is served over plain http (no service worker = no install).
let installMode = '';
let deferredInstallPrompt = null;
function isStandaloneDisplay() {
  try { if (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches) return true; } catch (e) { }
  return window.navigator.standalone === true;
}
function installWasDismissed() {
  try { return localStorage.getItem('srcbox_install_dismissed') === '1'; } catch (e) { return false; }
}
function initInstall() {
  var fab = document.getElementById('installFab');
  var fs = document.getElementById('fsFab');
  // Fullscreen works on plain http too (the Fullscreen API needs no secure context), so the button stays
  // available for phones that open the remote as a plain home screen shortcut.
  if (fs) {
    if (browserFullscreenSupported()) { fs.title = T.btn_fullscreen || ''; fs.style.display = ''; }
    else fs.style.display = 'none';
  }
  if (!fab) return;
  if (isStandaloneDisplay() || installWasDismissed()) { fab.style.display = 'none'; return; }
  if (!window.isSecureContext) installMode = 'insecure';
  else if (/iphone|ipad|ipod/i.test(navigator.userAgent)) installMode = 'ios';
  else installMode = 'native';
  fab.textContent = T.install_short || '';
  fab.style.display = '';
}
function browserFullscreenSupported() {
  var el = document.documentElement;
  return !!(el.requestFullscreen || el.webkitRequestFullscreen);
}
function toggleBrowserFullscreen() {
  try {
    var el = document.documentElement;
    if (document.fullscreenElement || document.webkitFullscreenElement) {
      var exit = document.exitFullscreen || document.webkitExitFullscreen;
      if (exit) exit.call(document);
    } else {
      var enter = el.requestFullscreen || el.webkitRequestFullscreen;
      if (enter) enter.call(el);
    }
  } catch (e) { }
}
window.addEventListener('beforeinstallprompt', function (e) { e.preventDefault(); deferredInstallPrompt = e; });
window.addEventListener('appinstalled', function () {
  var fab = document.getElementById('installFab'); if (fab) fab.style.display = 'none';
});
function noCertHint() {
  var ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
  return ios ? (T.install_nocert_ios || '') : (T.install_nocert_android || '');
}
function certLinks() {
  return '<br/><br/><a href=""/cert"">' + (T.install_cert || '') + '</a>'
       + ' · <a href=""/cert.pem"">' + (T.install_cert_pem || '') + '</a>'
       + ' · <a href=""/cert.mobileconfig"">' + (T.install_cert_ios || '') + '</a>';
}
function showInstallModal() {
  var modal = document.getElementById('installModal'); if (!modal) return;
  var title = document.getElementById('installTitle');
  var bodyEl = document.getElementById('installBody');
  var later = document.getElementById('installLater');
  var go = document.getElementById('installGo');
  if (title) title.textContent = T.install_title || '';
  if (later) { later.textContent = T.install_later || ''; later.style.display = ''; }
  if (go) go.style.display = 'none';
  if (installMode === 'ios') {
    if (bodyEl) bodyEl.textContent = T.install_ios || '';
    if (go) { go.textContent = T.btn_confirm || 'OK'; go.style.display = ''; }
  } else if (installMode === 'insecure') {
    if (bodyEl) bodyEl.innerHTML = (T.install_insecure || '') + '<br/><br/>' + (T.install_cert_steps || '')
      + certLinks() + '<br/><br/>' + noCertHint();
    if (go && HTTPS_PORT > 0) { go.textContent = T.install_https_open || ''; go.style.display = ''; }
  } else if (deferredInstallPrompt) {
    if (bodyEl) bodyEl.textContent = T.install_android || '';
    if (go) { go.textContent = T.install_go || ''; go.style.display = ''; }
  } else {
    // No captured beforeinstallprompt: Chrome only fires it once the page is installable, so point the
    // user at the browser menu entry instead of offering a button that could never do anything.
    if (bodyEl) bodyEl.innerHTML = (T.install_manual || '') + '<br/><br/>' + noCertHint() + certLinks();
    if (go) { go.textContent = T.btn_confirm || 'OK'; go.style.display = ''; }
  }
  modal.classList.add('show');
}
function closeInstallModal() {
  var modal = document.getElementById('installModal'); if (modal) modal.classList.remove('show');
}
function dismissInstall() {
  try { localStorage.setItem('srcbox_install_dismissed', '1'); } catch (e) { }
  var fab = document.getElementById('installFab'); if (fab) fab.style.display = 'none';
  closeInstallModal();
}
function openSecureRemote() {
  if (!HTTPS_PORT) { closeInstallModal(); return; }
  window.location.href = 'https://' + window.location.hostname + ':' + HTTPS_PORT + '/';
}
function doInstall() {
  if (installMode === 'native' && deferredInstallPrompt) {
    var prompt = deferredInstallPrompt;
    deferredInstallPrompt = null;
    prompt.prompt();
    if (prompt.userChoice && prompt.userChoice.then) prompt.userChoice.then(function () { closeInstallModal(); });
    return;
  }
  if (installMode === 'insecure') { openSecureRemote(); return; }
  closeInstallModal();
}

// ---------------------------------------------------------------- voice control
// Recognition runs on the phone (Web Speech API) and the meaning is resolved by the server, so the
// grammar, the channel names and the custom rules have exactly one implementation.
var VoiceSR = window.SpeechRecognition || window.webkitSpeechRecognition;
var voiceRec = null;
var voiceListening = false;
var voiceBusy = false;
var voiceHandsFree = false;
var voiceLastOptions = [];

function voiceSupported() { return !!VoiceSR && window.isSecureContext !== false; }

function voiceWakeWords() {
var list = (VOICE.wake && VOICE.wake.length) ? VOICE.wake : ['遥控器', '小箱', 'srcbox', 'remote', 'пульт'];
return list;
}

function voiceNormalize(s) {
return (s || '').toLowerCase().replace(/[\s,，。.、!！?？:：;；\u0027\u0022\u201c\u201d\u2018\u2019()（）·-]/g, '');
}

// Returns the sentence without the wake word, or null when the wake word was not spoken.
function voiceStripWake(text) {
var n = voiceNormalize(text);
var words = voiceWakeWords();
for (var i = 0; i < words.length; i++) {
var w = voiceNormalize(words[i]);
if (!w) continue;
var idx = n.indexOf(w);
if (idx >= 0) return n.substring(0, idx) + n.substring(idx + w.length);
}
return null;
}

function voiceSet(id, text) { var el = document.getElementById(id); if (el) el.textContent = text || ''; }
function setVoiceStatus(text) { voiceSet('voiceStatus', text); }
function setVoiceHeard(text) { voiceSet('voiceHeard', text ? ((T.voice_heard || '') + ': ' + text) : ''); }
function voiceMicClass(on) {
var el = document.getElementById('voiceMicBtn');
if (el) el.className = on ? 'voice-mic listening' : 'voice-mic';
}

function voiceMakeRecognition() {
if (voiceRec || !VoiceSR) return voiceRec;
var rec = new VoiceSR();
rec.interimResults = false;
rec.maxAlternatives = 1;
rec.onresult = function(e) {
var text = '';
for (var i = e.resultIndex; i < e.results.length; i++) {
if (e.results[i].isFinal) text += e.results[i][0].transcript;
}
if (!text) return;
if (!voiceHandsFree) { sendVoice(text); return; }
var stripped = voiceStripWake(text);
if (stripped === null) return; // ambient speech: hands-free only reacts to the wake word
setVoiceHeard(stripped || text);
if (stripped) sendVoice(stripped); else setVoiceStatus(T.voice_listening);
};
rec.onerror = function(ev) {
voiceListening = false; voiceMicClass(false);
var code = ev && ev.error ? ev.error : '';
if (code === 'not-allowed' || code === 'service-not-allowed') {
voiceHandsFree = false; voiceRefreshHandsFree();
setVoiceStatus(T.voice_secure_needed);
} else if (code === 'no-speech' || code === 'aborted') {
if (!voiceBusy) setVoiceStatus(voiceHandsFree ? T.voice_listening : T.voice_hold);
} else {
setVoiceStatus(T.voice_error);
}
};
rec.onend = function() {
voiceListening = false; voiceMicClass(false);
if (voiceHandsFree) { setTimeout(function() { if (voiceHandsFree && !voiceListening) startVoiceListening(); }, 400); return; }
if (!voiceBusy) setVoiceStatus(T.voice_hold);
};
voiceRec = rec;
return rec;
}

function startVoiceListening() {
if (!voiceSupported()) { setVoiceStatus(window.isSecureContext === false ? T.voice_secure_needed : T.voice_unsupported); return; }
var rec = voiceMakeRecognition();
if (!rec) { setVoiceStatus(T.voice_unsupported); return; }
rec.lang = voiceLang();
// Continuous mode only for hands-free: one-shot recognition is what iOS and Android both do reliably.
rec.continuous = voiceHandsFree;
try { rec.start(); } catch (err) { return; }
voiceListening = true; voiceMicClass(true);
setVoiceStatus(T.voice_listening);
}

function stopVoiceListening() {
voiceHandsFree = false; voiceRefreshHandsFree();
if (voiceRec && voiceListening) { try { voiceRec.stop(); } catch (err) { } }
voiceListening = false; voiceMicClass(false);
setVoiceStatus(T.voice_hold);
}

function voiceRefreshHandsFree() {
var b = document.getElementById('voiceHandsFree');
if (b) b.className = voiceHandsFree ? 'on' : '';
}

function voiceMicTap() {
if (voiceListening) { stopVoiceListening(); return; }
startVoiceListening();
}

function toggleHandsFree() {
if (voiceListening) { stopVoiceListening(); return; }
if (!voiceSupported()) { setVoiceStatus(window.isSecureContext === false ? T.voice_secure_needed : T.voice_unsupported); return; }
voiceHandsFree = true; voiceRefreshHandsFree();
startVoiceListening();
}

function toggleVoice() {
var ov = document.getElementById('voiceOverlay');
if (!ov) return;
if (ov.classList.contains('show')) { closeVoice(); return; }
openVoice();
}

function openVoice() {
var ov = document.getElementById('voiceOverlay');
if (ov) ov.classList.add('show');
voiceBusy = false;
setVoiceStatus(VOICE.enabled === false ? T.voice_unsupported : T.voice_hold);
setVoiceHeard('');
renderVoiceOptions([]);
applyVoiceText();
// The settings may ask for an always-open microphone; still only start it from this user gesture.
if (VOICE.handsFree && voiceSupported() && !voiceListening) {
voiceHandsFree = true; voiceRefreshHandsFree();
startVoiceListening();
}
}

function closeVoice() {
var ov = document.getElementById('voiceOverlay');
if (ov) ov.classList.remove('show');
stopVoiceListening();
if (window.speechSynthesis) { try { window.speechSynthesis.cancel(); } catch (err) { } }
}

function submitVoiceInput() {
var input = document.getElementById('voiceInput');
var text = input ? input.value.replace(/^\s+|\s+$/g, '') : '';
if (!text) return;
if (input) input.value = '';
sendVoice(text);
}

function sendVoice(text) {
if (!text) return;
voiceBusy = true;
setVoiceStatus(T.voice_thinking);
setVoiceHeard(text);
renderVoiceOptions([]);
send('voiceCommand', { text: text, lang: voiceLang() });
}

function handleVoiceReply(reply) {
voiceBusy = false;
if (reply.spoken) { setVoiceStatus(reply.spoken); speakVoice(reply.spoken); }
else setVoiceStatus(reply.success ? T.voice_hold : T.voice_not_understood);
renderVoiceOptions(reply.options || []);
if (reply.executed) loadStatus();
}

function renderVoiceOptions(options) {
voiceLastOptions = options || [];
var box = document.getElementById('voiceOptions');
if (!box) return;
box.innerHTML = '';
for (var i = 0; i < voiceLastOptions.length; i++) {
(function(idx) {
var o = voiceLastOptions[idx];
var b = document.createElement('button');
b.className = 'voice-option';
b.textContent = o.title || o.id || '';
if (o.subtitle) {
var sub = document.createElement('span');
sub.className = 'vo-sub';
sub.textContent = o.subtitle;
b.appendChild(sub);
}
b.onclick = function() { pickVoiceOption(idx); };
box.appendChild(b);
})(i);
}
}

// A tapped candidate replays an action the page already implements, so nothing new can execute here.
function pickVoiceOption(idx) {
var o = voiceLastOptions[idx];
if (!o) return;
if (o.action) send(o.action, o.payload || {});
renderVoiceOptions([]);
closeVoice();
setTimeout(function() { loadStatus(); }, 400);
}

function speakVoice(text) {
if (!VOICE.speak || !text || !window.speechSynthesis) return;
try {
window.speechSynthesis.cancel();
var u = new SpeechSynthesisUtterance(text);
u.lang = voiceLang();
window.speechSynthesis.speak(u);
} catch (err) { }
}

// Opening or refreshing the page must not move the page scrollbar; the channel and EPG lists
// locate the current entry inside their own scroll area instead (see locateInList).
if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
window.addEventListener('load', function () { window.scrollTo(0, 0); });

// Restore the theme and the remote/full view before connecting: the saved choice must survive a
// refresh even when the WebSocket is slow or never opens, otherwise the page flashes the default.
loadTheme();
loadRemoteView();
applyStaticText();

// Initialize the install entry before connecting: a failed WebSocket connect must not hide it.
applyVoiceText();
initInstall();
connect();

// Register Service Worker for PWA install support
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').then(function(reg) {
    console.log('Service Worker registered:', reg.scope);
  }).catch(function(err) {
    console.log('Service Worker registration failed:', err);
  });
}
</script>
</body>
</html>";
        }
    }

    public class WebRemoteStatus
    {
        public bool Playing { get; set; }
        public string Mode { get; set; } = "Stopped";
        public string ModeText { get; set; } = "已停止";
        public WebRemoteChannel? Channel { get; set; }
        public double Volume { get; set; }
        public bool Muted { get; set; }
        public double Speed { get; set; } = 1.0;
        public WebRemoteProgram? CurrentProgram { get; set; }
        public WebRemoteTimeshift? Timeshift { get; set; }
        public bool TimeshiftEnabled { get; set; }
    }

    public class WebRemoteChannel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>1-based channel number, assigned by the server so the page and the local
        /// channel-number overlay always agree.</summary>
        public int Number { get; set; }
        public string? Logo { get; set; }
        public string? CurrentProgram { get; set; }
        public string? CurrentTime { get; set; }
    }

    public class WebRemoteProgram
    {
        public string Name { get; set; } = "";
        public string Start { get; set; } = "";      // HH:mm
        public string End { get; set; } = "";        // HH:mm
        public string StartISO { get; set; } = "";   // yyyy-MM-ddTHH:mm:ss
        public string EndISO { get; set; } = "";     // yyyy-MM-ddTHH:mm:ss
        public string Date { get; set; } = "";       // MM-dd (显示日期)
        public bool IsCurrent { get; set; }
        /// <summary>True when this channel can replay the programme (catch-up source or timeshift url),
        /// so the page only offers the replay button where it can actually work.</summary>
        public bool CanReplay { get; set; }
        // 微标类型: "live"=正在播出 "replay"=回看 "reminder"=预约 "next"=下一节目
        public string? Badge { get; set; }
        public string? BadgeHtml { get; set; }
    }

    public class WebRemoteTimeshift
    {
        public bool Active { get; set; }
        public string? Cursor { get; set; }
        public string? Range { get; set; }
        // The seekable window in epoch milliseconds plus the play position as an offset from the window
        // start: the page draws its scrubber timeline from these numbers instead of parsing the labels.
        public long? FromMs { get; set; }
        public long? ToMs { get; set; }
        public double? PositionSec { get; set; }
    }

    public class WebRemoteChannelGroup
    {
        public string Name { get; set; } = "";
        public List<WebRemoteChannel> Channels { get; set; } = new();
        // Marks the synthetic favourites group so clients never match it by its localized name
        public bool IsFavorites { get; set; }
    }

    public class WebRemoteSource
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    public class WebRemoteReminder
    {
        public string Id { get; set; } = "";
        public string ChannelId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string ProgramTitle { get; set; } = "";
        public string StartAt { get; set; } = "";
        public string EndTime { get; set; } = "";
        public string Action { get; set; } = "";
        public bool Enabled { get; set; }
        public bool Completed { get; set; }
        public int? RecordDurationMin { get; set; }
    }

    public class WebRemoteRecording
    {
        public string Id { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string ProgramTitle { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusLabel { get; set; } = "";
        public string ScheduledStart { get; set; } = "";
        public string ScheduledEnd { get; set; } = "";
        public string SizeLabel { get; set; } = "";
        public string? FilePath { get; set; }
    }
}