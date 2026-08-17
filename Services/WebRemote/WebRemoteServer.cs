using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services.WebRemote
{
    public class WebRemoteServer : IDisposable
    {
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private readonly List<WebSocket> _clients = new();
        private readonly object _clientsLock = new();

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        // 密码保护
        private bool _requirePassword;
        private string _password = "";
        // NEW-13: ConcurrentDictionary instead of HashSet + lock, eliminating data race
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _authenticatedTokens = new();

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
        public Action<string, string, string, string>? ReplayProgramCallback { get; set; }
        public Func<List<WebRemoteReminder>>? GetRemindersCallback { get; set; }
        public Func<string, string, string, string, string, int, int?, string>? AddReminderCallback { get; set; }
        public Action<string>? CancelReminderCallback { get; set; }
        public Func<List<WebRemoteRecording>>? GetRecordingsCallback { get; set; }
        public Action<string>? StopRecordingCallback { get; set; }
        public Action<string>? DeleteRecordingCallback { get; set; }

        public void Start(int port, bool requirePassword = false, string password = "")
        {
            if (IsRunning) return;

            Port = port;
            _requirePassword = requirePassword;
            _password = password ?? "";
            _authenticatedTokens.Clear();
            _cts = new CancellationTokenSource();

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                IsRunning = true;
                _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
                Logger.Debug($"[WebRemote] Server started on port {port}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to start server: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

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
            IsRunning = false;
            Logger.Debug("[WebRemote] Server stopped");
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Logger.Error($"[WebRemote] Accept error: {ex.Message}"); }
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
        {
            using (tcpClient)
            {
                try
                {
                    var stream = tcpClient.GetStream();
                    stream.ReadTimeout = 5000;
                    Logger.Debug($"[WebRemote] Client connected from {tcpClient.Client.RemoteEndPoint}");

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

        private async Task ServeHttpPageAsync(TcpClient tcpClient, NetworkStream stream, string request, CancellationToken ct)
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

                Logger.Debug("[WebRemote] Sending HTML page");
                var lang = ParseAcceptLanguage(request);
                var html = GetRemoteHtml(lang);
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

        private async Task ServeLogoAsync(TcpClient tcpClient, NetworkStream stream, string request, CancellationToken ct)
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
            finally { tcpClient.Close(); }
        }

        private async Task SendErrorAsync(NetworkStream stream, string message, CancellationToken ct)
        {
            var body = $"<html><body><h1>{message}</h1></body></html>";
            var header = $"HTTP/1.1 {message}\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n";
            var headerBuf = Encoding.UTF8.GetBytes(header);
            var bodyBuf = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(headerBuf, ct);
            await stream.WriteAsync(bodyBuf, ct);
        }

        private async Task HandleWebSocketAsync(TcpClient tcpClient, NetworkStream stream, string request, CancellationToken ct)
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
                    var token = ws.SubProtocol ?? "";
                    bool isAuth = _authenticatedTokens.ContainsKey(token); // NEW-13: lock-free
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
                            _authenticatedTokens.TryAdd(ws.SubProtocol ?? "", true); // NEW-13: lock-free
                            Logger.Debug("[WebRemote] Client authenticated successfully");
                        }
                        else
                        {
                            Logger.Warn($"[WebRemote] Authentication failed with password: {pwd}");
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
                        result = new { groups, favorites = groups.FirstOrDefault(g => g.Name == "我的收藏")?.Channels ?? new List<WebRemoteChannel>() };
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

        private static string GetRemoteHtml(string lang = "zh-CN")
        {
            var strings = WebRemoteStrings.Get(lang);
            var stringsJson = System.Text.Json.JsonSerializer.Serialize(strings);
            var langAttr = (lang ?? "zh-CN").ToLowerInvariant().StartsWith("en") ? "en"
                : (lang ?? "zh-CN").ToLowerInvariant().StartsWith("ru") ? "ru"
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
                .Replace("__LIVE__", strings.TryGetValue("live_badge", out var t7) ? t7 : "LIVE")
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
                .Replace("__SEC_CHANNELS__", strings.TryGetValue("section_channels", out var t20) ? t20 : "")
                .Replace("__LOADING__", strings.TryGetValue("loading", out var t21) ? t21 : "")
                .Replace("__SEC_EPG__", strings.TryGetValue("section_epg", out var t22) ? t22 : "")
                .Replace("__EPG_SELECT__", strings.TryGetValue("epg_select", out var t23) ? t23 : "")
                .Replace("__EPG_EMPTY__", strings.TryGetValue("epg_empty", out var t24) ? t24 : "")
                .Replace("__EXIT_CONFIRM__", strings.TryGetValue("exit_confirm", out var t25) ? t25 : "")
                .Replace("__CONNECTING__", strings.TryGetValue("connecting", out var t26) ? t26 : "")
                .Replace("__FAV_STAR__", strings.TryGetValue("fav_star", out var t27) ? t27 : "*")
                .Replace("__FAV_GROUP__", strings.TryGetValue("fav_group", out var t28) ? t28 : "Favorites")
                .Replace("__SEC_SOURCES__", strings.TryGetValue("sec_sources", out var t29) ? t29 : "")
                .Replace("__SEC_REMINDERS__", strings.TryGetValue("sec_reminders", out var t30) ? t30 : "")
                .Replace("__SEC_RECORDINGS__", strings.TryGetValue("sec_recordings", out var t31) ? t31 : "")
                .Replace("__BTN_ADD_SOURCE__", strings.TryGetValue("btn_add_source", out var t32) ? t32 : "")
                .Replace("__MODAL_ADD_SOURCE__", strings.TryGetValue("modal_add_source", out var t33) ? t33 : "")
                .Replace("__LABEL_SOURCE_NAME__", strings.TryGetValue("label_source_name", out var t34) ? t34 : "")
                .Replace("__LABEL_SOURCE_URL__", strings.TryGetValue("label_source_url", out var t35) ? t35 : "")
                .Replace("__SOURCE_EMPTY__", strings.TryGetValue("source_empty", out var t36) ? t36 : "")
                .Replace("__SOURCE_ACTIVE__", strings.TryGetValue("source_active", out var t37) ? t37 : "")
                .Replace("__SOURCE_SELECT__", strings.TryGetValue("source_select", out var t38) ? t38 : "")
                .Replace("__SOURCE_REMOVE__", strings.TryGetValue("source_remove", out var t39) ? t39 : "")
                .Replace("__CONFIRM_REMOVE_SOURCE__", strings.TryGetValue("confirm_remove_source", out var t40) ? t40 : "")
                .Replace("__RECORDING_EMPTY__", strings.TryGetValue("recording_empty", out var t41) ? t41 : "")
                .Replace("__RECORDING_STOP__", strings.TryGetValue("recording_stop", out var t42) ? t42 : "")
                .Replace("__RECORDING_DELETE__", strings.TryGetValue("recording_delete", out var t43) ? t43 : "")
                .Replace("__REMINDER_EMPTY__", strings.TryGetValue("reminder_empty", out var t44) ? t44 : "")
                .Replace("__REMINDER_CANCEL__", strings.TryGetValue("reminder_cancel", out var t45) ? t45 : "")
                .Replace("__MODAL_ADD_REMINDER__", strings.TryGetValue("modal_add_reminder", out var t46) ? t46 : "")
                .Replace("__LABEL_REMINDER_ACTION__", strings.TryGetValue("label_reminder_action", out var t47) ? t47 : "")
                .Replace("__ACTION_PLAY__", strings.TryGetValue("action_play", out var t48) ? t48 : "")
                .Replace("__ACTION_RECORD_FRONT__", strings.TryGetValue("action_record_front", out var t49) ? t49 : "")
                .Replace("__ACTION_RECORD_BACK__", strings.TryGetValue("action_record_back", out var t50) ? t50 : "")
                .Replace("__ACTION_NOTIFY__", strings.TryGetValue("action_notify", out var t51) ? t51 : "")
                .Replace("__LABEL_PRE_ALERT__", strings.TryGetValue("label_pre_alert", out var t52) ? t52 : "")
                .Replace("__BTN_CANCEL__", strings.TryGetValue("btn_cancel", out var t53) ? t53 : "")
                .Replace("__BTN_CONFIRM__", strings.TryGetValue("btn_confirm", out var t54) ? t54 : "")
                .Replace("__BTN_REPLAY__", strings.TryGetValue("btn_replay", out var t55) ? t55 : "")
                .Replace("__BTN_TIMESHIFT__", strings.TryGetValue("btn_timeshift", out var t56) ? t56 : "")
                .Replace("__BTN_TIMESHIFT_ON__", strings.TryGetValue("btn_timeshift_on", out var t57) ? t57 : "")
                .Replace("__BTN_TIMESHIFT_OFF__", strings.TryGetValue("btn_timeshift_off", out var t58) ? t58 : "")
                .Replace("__BTN_REMIND__", strings.TryGetValue("btn_remind", out var t59) ? t59 : "")
                .Replace("__BTN_RECORD__", strings.TryGetValue("btn_record", out var t60) ? t60 : "")
                .Replace("__STATUS_CANCELLED__", strings.TryGetValue("status_cancelled", out var t61) ? t61 : "");
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
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
<title>__TITLE__</title>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: linear-gradient(135deg, #0f0c29 0%, #1a1a2e 50%, #24243e 100%); color: #eee; min-height: 100vh; overflow-x: hidden; }
.container { max-width: 100%; margin: 0 auto; padding: 12px; }
body.light-theme { background: linear-gradient(135deg, #f5f7fa 0%, #e4e8ec 50%, #d0d5dc 100%); color: #222; }
body.light-theme .header { background: rgba(0,0,0,0.08); }
body.light-theme .header h1 { color: #111; }
.theme-toggle { background: #333; border: none; border-radius: 8px; padding: 8px 12px; cursor: pointer; font-size: 14px; color: #fff; transition: all 0.2s; }
.theme-toggle:hover { background: #444; }
body.light-theme .theme-toggle { background: rgba(0,0,0,0.12); color: #222; }
body.light-theme .theme-toggle:hover { background: rgba(0,0,0,0.2); }
body.light-theme .now-playing { background: rgba(0,0,0,0.08); }
body.light-theme .now-playing-channel { color: #333; font-weight: 600; }
body.light-theme .now-playing-sep { color: #666; }
body.light-theme .now-playing-program { color: #555; font-weight: 500; }
body.light-theme .now-playing-program-time { color: #777; }
body.light-theme .controls-wrapper { background: rgba(0,0,0,0.06); border-radius: 12px; }
body.light-theme .btn { background: rgba(0,0,0,0.12) !important; color: #000 !important; font-weight: 600; }
body.light-theme .btn:active { background: rgba(0,0,0,0.18) !important; }
body.light-theme .btn span { color: #000 !important; }
body.light-theme .btn svg { fill: #000 !important; }
body.light-theme .btn svg path { fill: #000 !important; }
body.light-theme .volume-control { background: rgba(0,0,0,0.08); }
body.light-theme .volume-value { color: #1a6b3a; }
body.light-theme .channel-section { background: rgba(0,0,0,0.06); }
body.light-theme .section-title { color: #555; font-weight: 600; }
body.light-theme .channel-item { background: rgba(0,0,0,0.08); }
body.light-theme .channel-item:hover { background: rgba(0,0,0,0.14); }
body.light-theme .channel-item .name { color: #333; font-weight: 500; }
body.light-theme .channel-item .program { color: #1a6b3a; }
body.light-theme .channel-item .time { color: #666; }
body.light-theme .epg-section { background: rgba(0,0,0,0.06); }
body.light-theme .epg-header-title { color: #555; font-weight: 600; }
body.light-theme .epg-channel-name { color: #1a6b3a; font-weight: 600; }
body.light-theme .epg-timeline { color: #555; }
body.light-theme .epg-current-time { color: #cc3344; font-weight: 600; }
body.light-theme .epg-item { background: rgba(0,0,0,0.04); }
body.light-theme .epg-item:hover { background: rgba(0,0,0,0.1); }
body.light-theme .epg-item.current { background: rgba(46,213,115,0.25); }
body.light-theme .epg-item-live { border-bottom-color: #ff4757; }
body.light-theme .epg-item-replay { border-bottom-color: #2ed573; }
body.light-theme .epg-item-reminder { border-bottom-color: #1a6b3a; }
body.light-theme .epg-name { color: #222; font-weight: 500; }
body.light-theme .epg-desc { color: #666; }
body.light-theme .loading { color: #666; }
body.light-theme .epg-time { color: #555; }
body.light-theme .epg-time-end { color: #777; }
body.light-theme .epg-badge-live { background: #ff4757; }
body.light-theme .epg-badge-replay { background: #2ed573; }
body.light-theme .epg-badge-reminder { background: #1a6b3a; }
body.light-theme .epg-badge-next { }
body.light-theme .epg-content { color: #222; }
/* Source Management light-theme */
body.light-theme .source-item { background: rgba(0,0,0,0.08); }
body.light-theme .source-item:hover { background: rgba(0,0,0,0.14); }
body.light-theme .source-item .sname { color: #333; }
body.light-theme .source-item .surl { color: #666; }
body.light-theme .source-item .sbtns button { background: rgba(0,0,0,0.12); color: #000; }
body.light-theme .source-item .sbtns button:hover { background: rgba(0,0,0,0.2); }
body.light-theme .source-item .sactive { color: #1a6b3a; }
.header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; padding: 8px 12px; background: #000; border-radius: 12px; }
.header h1 { font-size: 16px; margin: 0; display: flex; align-items: center; gap: 8px; }
.live-badge { background: #ff4757; color: white; padding: 2px 8px; border-radius: 10px; font-size: 10px; font-weight: bold; animation: pulse 2s infinite; }
@keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.6; } }
.now-playing { background: #000; border-radius: 10px; padding: 16px; margin-bottom: 12px; position: relative; overflow: hidden; }
.now-playing-info { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.now-playing-left { display: flex; flex-direction: column; gap: 2px; }
.now-playing-channel-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.now-playing-channel { font-size: 20px; font-weight: bold; color: #eee; }
.now-playing-sep { color: #666; font-size: 16px; }
.now-playing-program { font-size: 14px; color: #7bed9f; }
.now-playing-program-time { font-size: 12px; color: #888; margin-left: 4px; }
.now-playing-time { font-size: 12px; color: #888; }
.now-playing-mode { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 10px; font-weight: bold; flex-shrink: 0; }
.mode-live { background: #ff4757; } .mode-replay { background: #ffa502; }
.mode-timeshift { background: #1e90ff; } .mode-stopped { background: #57606f; }
.controls-wrapper { background: #000; border-radius: 12px; padding: 12px; margin-bottom: 12px; }
.controls { display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; }
.controls + .controls { margin-top: 6px; }
.btn { padding: 12px 8px; border: none; border-radius: 10px; cursor: pointer; transition: all 0.2s; background: #1a1a1a; color: #fff; display: flex; flex-direction: column; align-items: center; gap: 2px; }
.btn:active { transform: scale(0.95); background: #2a2a2a; }
.btn span { font-size: 10px; color: #fff; }
.icon { width: 20px; height: 20px; fill: currentColor; }
.volume-icon { width: 20px; height: 20px; fill: currentColor; }
.volume-control { background: #000; border-radius: 12px; padding: 12px; margin-bottom: 12px; display: flex; align-items: center; gap: 10px; }
.volume-icon { font-size: 20px; }
.volume-bar { flex: 1; height: 6px; background: rgba(255,255,255,0.1); border-radius: 3px; cursor: pointer; position: relative; }
.volume-fill { height: 100%; background: linear-gradient(90deg, #2ed573, #7bed9f); border-radius: 3px; transition: width 0.3s; }
.volume-value { min-width: 40px; text-align: right; font-size: 12px; color: #7bed9f; }
.channel-section { background: #000; border-radius: 12px; padding: 12px; margin-bottom: 12px; }
.section-title { font-size: 12px; color: #888; margin-bottom: 10px; text-transform: uppercase; letter-spacing: 1px; }
.channel-grid { display: grid; grid-template-columns: repeat(1, 1fr); gap: 6px; max-height: 320px; overflow-y: auto; }
.channel-item { background: #1a1a1a; border-radius: 10px; padding: 10px 12px; text-align: left; cursor: pointer; transition: all 0.2s; border: 2px solid transparent; display: flex; align-items: center; gap: 10px; }
.channel-item:hover { background: #2a2a2a; }
.channel-item.active { background: rgba(46,213,115,0.2); border-color: #2ed573; }
.channel-item .logo { font-size: 26px; flex-shrink: 0; width: 48px; height: 48px; display: flex; align-items: center; justify-content: center; }
.channel-item .logo img { width: 48px; height: 48px; object-fit: contain; }
.channel-item .info { flex: 1; min-width: 0; }
.channel-item .name { font-size: 13px; color: #eee; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.channel-item .program { font-size: 11px; color: #7bed9f; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-top: 2px; }
.channel-item .time { font-size: 10px; color: #888; margin-left: auto; flex-shrink: 0; white-space: nowrap; }
.epg-section { background: #000; border-radius: 12px; padding: 12px; }
.epg-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px; }
.epg-header-title { font-size: 12px; color: #888; text-transform: uppercase; letter-spacing: 1px; }
.epg-channel-name { font-size: 14px; color: #7bed9f; font-weight: bold; }
.epg-date-nav { display: flex; align-items: center; gap: 4px; }
.epg-nav-btn { background: #333; border: none; color: #fff; padding: 4px 8px; cursor: pointer; border-radius: 4px; font-size: 12px; }
.epg-nav-btn:hover { background: #444; }
.epg-date-display { font-size: 13px; color: #7bed9f; cursor: pointer; padding: 0 8px; min-width: 50px; text-align: center; }
body.light-theme .epg-nav-btn { background: rgba(0,0,0,0.12); color: #000; }
body.light-theme .epg-date-display { color: #1a6b3a; }
.epg-timeline { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; font-size: 11px; color: #666; }
.epg-current-time { color: #ff4757; font-weight: bold; }
.epg-list { max-height: 250px; overflow-y: auto; }
.epg-item { display: flex; align-items: stretch; background: rgba(255,255,255,0.03); border-radius: 8px; margin-bottom: 6px; overflow: hidden; cursor: pointer; transition: all 0.2s; }
.epg-item:hover { background: rgba(255,255,255,0.08); }
.epg-item.current { background: rgba(46,213,115,0.15); border-left: 3px solid #2ed573; }
.epg-item.past { opacity: 0.5; }
.epg-item-live { border-bottom: 3px solid #ff4757; }
.epg-item-replay { border-bottom: 3px solid #2ed573; }
.epg-item-reminder { border-bottom: 3px solid #1a6b3a; }
.epg-time { width: 80px; flex-shrink: 0; padding: 10px; font-size: 11px; color: #888; display: flex; flex-direction: column; justify-content: center; }
.epg-time-end { color: #666; font-size: 10px; margin-top: 2px; }
.epg-content { flex: 1; padding: 10px; font-size: 13px; color: #eee; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.epg-right-col { width: 80px; flex-shrink: 0; display: flex; align-items: center; justify-content: flex-end; gap: 4px; padding: 10px; }
.epg-desc { font-size: 10px; color: #666; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.epg-badge { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 10px; font-weight: bold; margin-left: 4px; }
.epg-badge-live { background: #ff4757; color: #fff; }
.epg-badge-replay { background: #2ed573; color: #fff; }
.epg-badge-reminder { background: #1a6b3a; color: #fff; }
.epg-badge-next { }
.loading { text-align: center; padding: 30px; color: #666; font-size: 12px; }
.loading::after { content: '...'; animation: dots 1.5s infinite; }
@keyframes dots { 0%,20% { content: '.'; } 40% { content: '..'; } 60%,100% { content: '...'; } }
#passwordOverlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.85); display: flex; align-items: center; justify-content: center; z-index: 9999; flex-direction: column; gap: 16px; }
#passwordOverlay h2 { color: #fff; font-size: 18px; margin: 0; }
#passwordOverlay input { padding: 10px 16px; font-size: 14px; border: none; border-radius: 8px; width: 200px; text-align: center; }
#passwordOverlay button { padding: 8px 24px; font-size: 14px; border: none; border-radius: 8px; background: #2ed573; color: #fff; cursor: pointer; }
#passwordOverlay button:hover { background: #26b863; }
#passwordOverlay .error { color: #ff4757; font-size: 12px; display: none; }
::-webkit-scrollbar { width: 4px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 2px; }
@media (max-width: 400px) {
.channel-grid { grid-template-columns: repeat(3, 1fr); }
.controls { grid-template-columns: repeat(5, 1fr); gap: 4px; }
.btn { padding: 10px 4px; font-size: 16px; }
}
/* EPG Action Buttons */
.epg-btn-live { background: #ff4757; border: none; border-radius: 4px; padding: 2px 6px; font-size: 10px; color: #fff; cursor: pointer; font-weight: bold; }
.epg-btn-replay { background: #2ed573; border: none; border-radius: 4px; padding: 2px 6px; font-size: 10px; color: #fff; cursor: pointer; font-weight: bold; }
.epg-btn-timeshift { background: #1e90ff; border: none; border-radius: 4px; padding: 2px 6px; font-size: 9px; color: #fff; cursor: pointer; }
.epg-btn-remind { background: #1a6b3a; border: none; border-radius: 4px; padding: 2px 6px; font-size: 10px; color: #fff; cursor: pointer; font-weight: bold; }
.epg-btn-rec { background: #ffa502; border: none; border-radius: 4px; padding: 2px 6px; font-size: 9px; color: #fff; cursor: pointer; }
.epg-item { display: flex; align-items: center; }
.epg-item .epg-time { min-width: 70px; }
.epg-item .epg-content { flex: 1; }
.epg-right-area { display: flex; align-items: center; justify-content: flex-end; }
.epg-actions-row { display: flex; gap: 4px; align-items: center; margin-right: 4px; }
.epg-item.past { opacity: 0.6; }
/* Source Management */
.source-section { background: #000; border-radius: 12px; padding: 12px; margin-bottom: 12px; }
.source-item { display: flex; align-items: center; justify-content: space-between; background: #1a1a1a; border-radius: 8px; padding: 8px 12px; margin-bottom: 6px; cursor: pointer; }
.source-item:hover { background: #2a2a2a; }
.source-item.active { background: rgba(46,213,115,0.2); border: 1px solid #2ed573; }
.source-item .sname { font-size: 12px; color: #ccc; }
.source-item .surl { font-size: 10px; color: #666; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 200px; }
.source-item .sactive { color: #2ed573; font-size: 10px; }
.source-item .sbtns { display: flex; gap: 4px; }
.source-item .sbtns button { padding: 4px 8px; font-size: 10px; background: #333; border: none; border-radius: 4px; color: #fff; cursor: pointer; }
.source-item .sbtns button:hover { background: #444; }
/* Reminder & Recording sections */
.reminder-section, .recording-section { background: #000; border-radius: 12px; padding: 12px; margin-bottom: 12px; }
.reminder-item, .recording-item { display: flex; align-items: center; justify-content: space-between; background: #1a1a1a; border-radius: 8px; padding: 8px 12px; margin-bottom: 6px; }
.reminder-item .rtime { font-size: 11px; color: #888; min-width: 120px; }
.reminder-item .rprogram { font-size: 12px; color: #eee; flex: 1; }
.reminder-item .raction { font-size: 10px; padding: 2px 6px; border-radius: 4px; }
.reminder-item .raction.play { background: #2ed573; }
.reminder-item .raction.record_front { background: #ffa502; }
.reminder-item .raction.record_back { background: #ff6b6b; }
.reminder-item .raction.notify { background: #57606f; }
.recording-item .rcstatus { font-size: 11px; color: #888; }
.recording-item .rcsize { font-size: 11px; color: #7bed9f; }
/* Modal */
.modal-overlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.7); display: none; align-items: center; justify-content: center; z-index: 10000; }
.modal-overlay.show { display: flex; }
.modal-box { background: #1a1a2e; border-radius: 12px; padding: 20px; min-width: 300px; max-width: 400px; }
.modal-box h3 { margin: 0 0 16px 0; font-size: 16px; color: #eee; }
.modal-box label { display: block; font-size: 12px; color: #888; margin-bottom: 4px; }
.modal-box input, .modal-box select { width: 100%; padding: 8px 12px; border: none; border-radius: 8px; background: #0f0f1a; color: #eee; font-size: 13px; margin-bottom: 12px; box-sizing: border-box; }
.modal-box .mbtns { display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px; }
.modal-box .mbtns button { padding: 8px 16px; border: none; border-radius: 8px; cursor: pointer; font-size: 13px; }
.modal-box .mbtns .mcancel { background: #333; color: #fff; }
.modal-box .mbtns .mok { background: #2ed573; color: #fff; }
.modal-box .rem-info { font-size: 13px; color: #eee; margin-bottom: 8px; }
/* Light theme for modals */
body.light-theme .modal-overlay { background: rgba(0,0,0,0.5); }
body.light-theme .modal-box { background: #f0f0f0; }
body.light-theme .modal-box h3 { color: #222; }
body.light-theme .modal-box label { color: #555; }
body.light-theme .modal-box input, body.light-theme .modal-box select { background: #fff; color: #222; }
body.light-theme .modal-box .mcancel { background: rgba(0,0,0,0.12); color: #000; }
body.light-theme .modal-box .mok { background: #2ed573; color: #fff; }
body.light-theme .modal-box .rem-info { color: #222; }
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
<h1>__TITLE_BAR__ <span class=""live-badge"" id=""liveBadge"" style=""display:none;"">__LIVE__</span></h1>
<button class=""theme-toggle"" id=""themeToggle"" onclick=""toggleTheme()"">__THEME__</button>
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

<script>
const T = __T_JSON__;
const FAV_GROUP = '__FAV_GROUP__';
const FAV_STAR = '__FAV_STAR__';
let ws; let currentChannelId = ''; let currentVolume = 70; let previousVolume = 70;
let isMuted = false; let channelList = []; let isDarkTheme = true; let isAuthenticated = false;

function applyStaticText() {
document.getElementById('statusMode').textContent = T.status_stopped;
document.getElementById('themeToggle').textContent = T.theme_dark;
document.title = T.title;
}

function connect() {
const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
ws = new WebSocket(protocol + '//' + location.host);
ws.onopen = function() { loadTheme(); applyStaticText(); loadStatus(); loadChannels(); };
ws.onclose = function() { isAuthenticated = false; setTimeout(connect, 3000); };
ws.onerror = function() { document.getElementById('channelName').textContent = T.connecting; };
ws.onmessage = function(e) {
try {
const data = JSON.parse(e.data);
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
function toggleTimeshift() { isTimeshiftEnabled = !isTimeshiftEnabled; send('setTimeshift', { enabled: isTimeshiftEnabled }); updateTimeshiftBtn(); }
function updateTimeshiftBtn() {
  var btn = document.getElementById('btnTimeshift');
  var path = document.getElementById('timeshiftPath');
  if (btn) { btn.className = 'btn' + (isTimeshiftEnabled ? ' active' : ''); }
  if (path) {
    // Play icon (triangle) when off, Pause icon (two bars) when on
    path.setAttribute('d', isTimeshiftEnabled ? 'M6 19h4V5H6v14zm8-14v14h4V5h-4z' : 'M8 5v14l11-7z');
  }
  var text = document.getElementById('btnTimeshiftText');
  if (text) text.textContent = isTimeshiftEnabled ? T.btn_timeshift_on : T.btn_timeshift_off;
}

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

function toggleTheme() {
isDarkTheme = !isDarkTheme;
document.body.classList.toggle('light-theme', !isDarkTheme);
document.getElementById('themeToggle').textContent = isDarkTheme ? T.theme_dark : T.theme_light;
try { localStorage.setItem('remote-theme', isDarkTheme ? 'dark' : 'light'); } catch (e) { }
}
function loadTheme() {
var saved = null; try { saved = localStorage.getItem('remote-theme'); } catch (e) { }
isDarkTheme = saved !== 'light';
document.body.classList.toggle('light-theme', !isDarkTheme);
document.getElementById('themeToggle').textContent = isDarkTheme ? T.theme_dark : T.theme_light;
}

function updateVolumeUI() {
document.getElementById('volFill').style.width = (isMuted ? 0 : currentVolume) + '%';
document.getElementById('volValue').textContent = isMuted ? 'x' : currentVolume + '%';
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
document.getElementById('liveBadge').textContent = T.live_badge;
document.getElementById('liveBadge').style.display = s.mode === 'Live' ? 'inline' : 'none';
currentChannelId = (s.channel && s.channel.id) || '';
if (!s.muted && s.volume > 0) previousVolume = s.volume;
currentVolume = s.muted ? previousVolume : (s.volume || 0);
isMuted = s.muted || false;
isTimeshiftEnabled = s.timeshiftEnabled || false;
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
if (data.favorites && data.favorites.length) { data.favorites.forEach(function(c) { channelList.push(c); addChannelItem(grid, c, true); }); }
if (data.groups && data.groups.length) { data.groups.forEach(function(g) { if (g.name === FAV_GROUP) return; if (g.channels) g.channels.forEach(function(c) { channelList.push(c); addChannelItem(grid, c); }); }); }
updateChannelActive();
}
function addChannelItem(grid, c, isFavorite) {
var div = document.createElement('div'); div.className = 'channel-item'; div.dataset.id = c.id;
var logoHtml;
if (c.logo && c.logo.length > 0) {
if (c.logo.indexOf('http') === 0) {
// External HTTP URL - use directly
logoHtml = '<img src=' + c.logo + ' style=width:48px;height:48px;object-fit:contain crossorigin=anonymous>';
} else if (c.logo.indexOf('file:///') === 0 || c.logo.indexOf('\\\\') === 0 || c.logo.indexOf('D:') === 0 || c.logo.indexOf('C:') === 0) {
// Local file path - convert to /logo/ endpoint
var localPath = c.logo.replace('file:///', '').replace(/\//g, '\\\\');
var encodedPath = encodeURIComponent(localPath).replace(/%5C/g, '/');
logoHtml = '<img src=/logo/' + encodedPath + ' style=width:48px;height:48px;object-fit:contain>';
} else {
// Assume it's a local path
var encodedPath = encodeURIComponent(c.logo).replace(/%5C/g, '/');
logoHtml = '<img src=/logo/' + encodedPath + ' style=width:48px;height:48px;object-fit:contain>';
}
} else {
logoHtml = '<span style=font-size:22px>' + (isFavorite ? FAV_STAR : 'TV') + '</span>';
}
var nameHtml = '<div class=name>' + (c.name || '') + '</div>';
var progHtml = '<div class=program>' + (c.currentProgram || '') + '</div>';
var timeHtml = '<div class=time>' + (c.currentTime || '') + '</div>';
div.innerHTML = '<div class=logo>' + logoHtml + '</div><div class=info>' + nameHtml + progHtml + '</div>' + timeHtml;
div.onclick = function() { changeChannel(c.id); };
grid.appendChild(div);
}
function renderEpg(data) {
var list = document.getElementById('epgList'); var channelName = document.getElementById('epgChannelName');
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
else if (isPast && isTimeshiftEnabled) {
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
}
function updateChannelActive() {
var items = document.querySelectorAll('.channel-item');
for (var i = 0; i < items.length; i++) {
var el = items[i];
if (el.dataset.id === currentChannelId) el.classList.add('active'); else el.classList.remove('active');
}
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
async function exitApp() { if (confirm(T.exit_confirm)) send('exit'); }
connect();
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
        // 微标类型: "live"=正在播出 "replay"=回看 "reminder"=预约 "next"=下一节目
        public string? Badge { get; set; }
        public string? BadgeHtml { get; set; }
    }

    public class WebRemoteTimeshift
    {
        public bool Active { get; set; }
        public string? Cursor { get; set; }
        public string? Range { get; set; }
    }

    public class WebRemoteChannelGroup
    {
        public string Name { get; set; } = "";
        public List<WebRemoteChannel> Channels { get; set; } = new();
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
