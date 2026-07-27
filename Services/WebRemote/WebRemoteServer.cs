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

        public Func<WebRemoteStatus>? GetStatusCallback { get; set; }
        public Func<List<WebRemoteChannelGroup>>? GetChannelsCallback { get; set; }
        public Func<string, List<WebRemoteProgram>>? GetEpgCallback { get; set; }
        public Action? PlayCallback { get; set; }
        public Action? PauseCallback { get; set; }
        public Action? StopCallback { get; set; }
        public Action<double>? SetVolumeCallback { get; set; }
        public Action<string>? ChangeChannelCallback { get; set; }
        public Action? ExitCallback { get; set; }
        public Action? FullscreenCallback { get; set; }
        public Action? SwitchSourceCallback { get; set; }

        public void Start(int port)
        {
            if (IsRunning) return;

            Port = port;
            _cts = new CancellationTokenSource();

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                IsRunning = true;
                _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
                Logger.Info($"[WebRemote] Server started on port {port}");
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
            Logger.Info("[WebRemote] Server stopped");
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
                    Logger.Info($"[WebRemote] Client connected from {tcpClient.Client.RemoteEndPoint}");

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
                    Logger.Info($"[WebRemote] Request length: {request.Length}, starts with: {request.Substring(0, Math.Min(20, request.Length)).Replace("\r\n", "\\r\\n")}");

                    // Check if it's a WebSocket upgrade request
                    bool isWebSocket = request.Contains("Upgrade:") && request.Contains("websocket");
                    Logger.Info($"[WebRemote] Is WebSocket upgrade: {isWebSocket}");

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
                Logger.Info("[WebRemote] Sending HTML page");
                var html = GetRemoteHtml();
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n";
                var headerBuf = Encoding.UTF8.GetBytes(header);
                var bodyBuf = Encoding.UTF8.GetBytes(html);
                await stream.WriteAsync(headerBuf, ct);
                await stream.WriteAsync(bodyBuf, ct);
                Logger.Info("[WebRemote] HTML page sent");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] ServeHttpPage error: {ex.Message}");
            }
            finally { tcpClient.Close(); }
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

                Logger.Info($"[WebRemote] WebSocket key: {(string.IsNullOrEmpty(key) ? "NOT FOUND" : "found")}");

                if (string.IsNullOrEmpty(key))
                {
                    tcpClient.Close();
                    return;
                }

                // WebSocket handshake response
                var acceptKey = Convert.ToBase64String(System.Security.Cryptography.SHA1.Create()
                    .ComputeHash(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var handshake = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);

                // Create WebSocket from raw connection
                ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromMinutes(30));

                lock (_clientsLock) { _clients.Add(ws); }
                Logger.Info($"[WebRemote] WebSocket client connected, state: {ws.State}");

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
                Logger.Info($"[WebRemote] Received message: {message}");
                var json = JsonSerializer.Deserialize<JsonElement>(message);
                if (!json.TryGetProperty("action", out var actionElem)) return;
                var action = actionElem.GetString() ?? "";
                Logger.Info($"[WebRemote] Action: {action}");

                object? result = null;

                switch (action)
                {
                    case "getStatus":
                        result = GetStatusCallback?.Invoke() ?? new WebRemoteStatus();
                        break;

                    case "getChannels":
                        var groups = GetChannelsCallback?.Invoke() ?? new List<WebRemoteChannelGroup>();
                        Logger.Info($"[WebRemote] getChannels returned {groups.Count} groups");
                        if (groups.Count > 0)
                        {
                            Logger.Info($"[WebRemote] First group: {groups[0].Name}, channels: {groups[0].Channels.Count}");
                        }
                        result = new { groups, favorites = groups.FirstOrDefault(g => g.Name == "我的收藏")?.Channels ?? new List<WebRemoteChannel>() };
                        break;

                    case "getEpg":
                        var channelId = "";
                        if (json.TryGetProperty("channelId", out var idElem))
                            channelId = idElem.GetString() ?? "";
                        result = new { channelId, programs = GetEpgCallback?.Invoke(channelId) ?? new List<WebRemoteProgram>() };
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

                    default:
                        result = new { error = "Unknown action" };
                        break;
                }

                var response = JsonSerializer.Serialize(result, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                Logger.Info($"[WebRemote] Sending response for {action}, length: {response.Length}, preview: {response.Substring(0, Math.Min(100, response.Length))}");
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

        private static string GetRemoteHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
    <title>SrcBox 遥控器</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: linear-gradient(135deg, #0f0c29 0%, #1a1a2e 50%, #24243e 100%); color: #eee; min-height: 100vh; overflow-x: hidden; }
        .container { max-width: 100%; margin: 0 auto; padding: 12px; }

        /* Header */
        .header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; padding: 8px 12px; background: rgba(255,255,255,0.05); border-radius: 12px; }
        .header h1 { font-size: 16px; margin: 0; display: flex; align-items: center; gap: 8px; }
        .live-badge { background: #ff4757; color: white; padding: 2px 8px; border-radius: 10px; font-size: 10px; font-weight: bold; animation: pulse 2s infinite; }
        @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.6; } }

        /* Now Playing Card */
        .now-playing { background: linear-gradient(145deg, #1e3a5f, #16213e); border-radius: 16px; padding: 16px; margin-bottom: 12px; position: relative; overflow: hidden; }
        .now-playing::before { content: ''; position: absolute; top: 0; left: 0; right: 0; height: 3px; background: linear-gradient(90deg, #ff4757, #ffa502, #2ed573, #1e90ff); }
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

        /* Control Buttons */
        .controls { display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; margin-bottom: 12px; }
        .btn { padding: 12px 8px; border: none; border-radius: 12px; font-size: 18px; cursor: pointer; transition: all 0.2s; background: rgba(255,255,255,0.1); color: #fff; display: flex; flex-direction: column; align-items: center; gap: 2px; }
        .btn:active { transform: scale(0.95); background: rgba(255,255,255,0.2); }
        .btn span { font-size: 10px; color: #aaa; }
        .btn-play { background: rgba(46,213,115,0.3); } .btn-play:active { background: rgba(46,213,115,0.5); }
        .btn-pause { background: rgba(255,165,2,0.3); } .btn-pause:active { background: rgba(255,165,2,0.5); }
        .btn-stop { background: rgba(255,71,87,0.3); } .btn-stop:active { background: rgba(255,71,87,0.5); }
        .btn-mute { background: rgba(87,96,111,0.3); } .btn-mute:active { background: rgba(87,96,111,0.5); }
        .btn-fullscreen { background: rgba(30,144,255,0.3); } .btn-fullscreen:active { background: rgba(30,144,255,0.5); }
        .btn-switch { background: rgba(155,89,182,0.3); } .btn-switch:active { background: rgba(155,89,182,0.5); }
        .btn-exit { background: rgba(238,90,36,0.3); } .btn-exit:active { background: rgba(238,90,36,0.5); }
        .btn-nav { background: rgba(55,66,250,0.3); } .btn-nav:active { background: rgba(55,66,250,0.5); }

        /* Volume */
        .volume-control { background: rgba(255,255,255,0.05); border-radius: 12px; padding: 12px; margin-bottom: 12px; display: flex; align-items: center; gap: 10px; }
        .volume-icon { font-size: 20px; }
        .volume-bar { flex: 1; height: 6px; background: rgba(255,255,255,0.1); border-radius: 3px; cursor: pointer; position: relative; }
        .volume-fill { height: 100%; background: linear-gradient(90deg, #2ed573, #7bed9f); border-radius: 3px; transition: width 0.3s; }
        .volume-value { min-width: 40px; text-align: right; font-size: 12px; color: #7bed9f; }

        /* Channel Grid */
        .channel-section { background: rgba(255,255,255,0.05); border-radius: 12px; padding: 12px; margin-bottom: 12px; }
        .section-title { font-size: 12px; color: #888; margin-bottom: 10px; text-transform: uppercase; letter-spacing: 1px; }
        .channel-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; max-height: 250px; overflow-y: auto; }
        .channel-item { background: rgba(255,255,255,0.08); border-radius: 10px; padding: 10px 6px; text-align: center; cursor: pointer; transition: all 0.2s; border: 2px solid transparent; }
        .channel-item:hover { background: rgba(255,255,255,0.15); }
        .channel-item.active { background: rgba(46,213,115,0.2); border-color: #2ed573; }
        .channel-item .logo { font-size: 22px; margin-bottom: 4px; }
        .channel-item .name { font-size: 10px; color: #ccc; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

        /* EPG Section - TV Guide Style */
        .epg-section { background: rgba(255,255,255,0.05); border-radius: 12px; padding: 12px; }
        .epg-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
        .epg-header-title { font-size: 12px; color: #888; text-transform: uppercase; letter-spacing: 1px; }
        .epg-channel-name { font-size: 14px; color: #7bed9f; font-weight: bold; }
        .epg-timeline { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; font-size: 11px; color: #666; }
        .epg-current-time { color: #ff4757; font-weight: bold; }

        .epg-list { max-height: 250px; overflow-y: auto; }
        .epg-item { display: flex; align-items: stretch; background: rgba(255,255,255,0.03); border-radius: 8px; margin-bottom: 6px; overflow: hidden; cursor: pointer; transition: all 0.2s; }
        .epg-item:hover { background: rgba(255,255,255,0.08); }
        .epg-item.current { background: rgba(46,213,115,0.15); border-left: 3px solid #2ed573; }
        .epg-item.past { opacity: 0.5; }
        .epg-time { padding: 10px 12px; font-size: 11px; color: #888; min-width: 70px; display: flex; flex-direction: column; justify-content: center; }
        .epg-time-end { color: #666; font-size: 10px; margin-top: 2px; }
        .epg-content { flex: 1; padding: 10px; display: flex; flex-direction: column; justify-content: center; }
        .epg-name { font-size: 13px; color: #eee; margin-bottom: 2px; }
        .epg-desc { font-size: 10px; color: #666; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .epg-badge { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 9px; font-weight: bold; margin-left: 6px; }
        .epg-badge-current { background: #2ed573; color: #fff; }
        .epg-badge-next { background: #ffa502; color: #fff; }

        /* Loading */
        .loading { text-align: center; padding: 30px; color: #666; font-size: 12px; }
        .loading::after { content: '...'; animation: dots 1.5s infinite; }
        @keyframes dots { 0%,20% { content: '.'; } 40% { content: '..'; } 60%,100% { content: '...'; } }

        /* Scrollbar */
        ::-webkit-scrollbar { width: 4px; }
        ::-webkit-scrollbar-track { background: transparent; }
        ::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 2px; }

        @media (max-width: 400px) {
            .channel-grid { grid-template-columns: repeat(3, 1fr); }
            .controls { grid-template-columns: repeat(5, 1fr); gap: 4px; }
            .btn { padding: 10px 4px; font-size: 16px; }
        }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>📺 SrcBox <span class=""live-badge"" id=""liveBadge"" style=""display:none;"">LIVE</span></h1>
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
                <span class=""now-playing-mode mode-stopped"" id=""statusMode"">已停止</span>
            </div>
        </div>

        <div class=""controls"">
            <button class=""btn btn-nav"" onclick=""prevChannel()"">⏮<span>上一台</span></button>
            <button class=""btn btn-play"" onclick=""play()"">▶<span>播放</span></button>
            <button class=""btn btn-pause"" onclick=""pause()"">⏸<span>暂停</span></button>
            <button class=""btn btn-stop"" onclick=""stop()"">⏹<span>停止</span></button>
            <button class=""btn btn-mute"" onclick=""toggleMute()"">🔊<span>静音</span></button>
        </div>
        <div class=""controls"">
            <button class=""btn btn-fullscreen"" onclick=""fullscreen()"">⛶<span>全屏</span></button>
            <button class=""btn btn-nav"" onclick=""nextChannel()"">⏭<span>下一台</span></button>
            <button class=""btn btn-switch"" onclick=""switchSource()"">🔀<span>换源</span></button>
            <button class=""btn btn-exit"" onclick=""exitApp()"">✕<span>退出</span></button>
            <button class=""btn"" onclick=""refreshData()"">🔄<span>刷新</span></button>
        </div>

        <div class=""volume-control"">
            <span class=""volume-icon"" id=""volIcon"">🔊</span>
            <div class=""volume-bar"" id=""volBar"" onclick=""setVolumeFromClick(event)""><div class=""volume-fill"" id=""volFill"" style=""width:70%""></div></div>
            <span class=""volume-value"" id=""volValue"">70%</span>
        </div>

        <div class=""channel-section"">
            <div class=""section-title"">频道列表</div>
            <div class=""channel-grid"" id=""channelGrid""><div class=""loading"">加载中</div></div>
        </div>

        <div class=""epg-section"">
            <div class=""epg-header"">
                <span class=""epg-header-title"">节目预告</span>
                <span class=""epg-channel-name"" id=""epgChannelName"">-</span>
            </div>
            <div class=""epg-timeline"">
                <span class=""epg-current-time"" id=""currentTime""></span>
            </div>
            <div class=""epg-list"" id=""epgList""><div class=""loading"">选择频道查看节目</div></div>
        </div>
    </div>

    <script>
        let ws;
        let currentChannelId = '';
        let currentVolume = 70;
        let isMuted = false;
        let channelList = [];
        let statusInterval;

        function connect() {
            const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
            ws = new WebSocket(protocol + '//' + location.host);
            ws.onopen = () => { loadStatus(); loadChannels(); statusInterval = setInterval(loadStatus, 5000); };
            ws.onclose = () => { clearInterval(statusInterval); setTimeout(connect, 3000); };
            ws.onerror = () => { document.getElementById('channelName').textContent = '连接失败'; };
            ws.onmessage = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    if (data.groups !== undefined) renderChannels(data);
                    else if (data.programs !== undefined) renderEpg(data);
                    else if (data.channel !== undefined || data.mode !== undefined) updateStatus(data);
                } catch (err) { console.error(err); }
            };
        }

        function send(action, data = {}) { if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ action, ...data })); }
        function loadStatus() { send('getStatus'); }
        function loadChannels() { send('getChannels'); }
        function loadEpg(channelId) { send('getEpg', { channelId }); }
        function refreshData() { loadStatus(); loadChannels(); if (currentChannelId) loadEpg(currentChannelId); }

        function updateStatus(s) {
            const modeMap = { Live:'直播', Replay:'回看', Timeshift:'时移', Recording:'录播', LocalFile:'本地', Stopped:'已停止' };
            const modeClass = { Live:'mode-live', Replay:'mode-replay', Timeshift:'mode-timeshift', Recording:'mode-recording', LocalFile:'mode-local', Stopped:'mode-stopped' };
            const el = document.getElementById('statusMode');
            el.textContent = modeMap[s.mode] || s.mode || '已停止';
            el.className = 'now-playing-mode ' + (modeClass[s.mode] || 'mode-stopped');
            document.getElementById('channelName').textContent = s.channel?.name || '未播放';
            document.getElementById('programName').textContent = s.currentProgram?.name || '';
            document.getElementById('programTime').textContent = s.currentProgram ? (s.currentProgram.start + ' - ' + s.currentProgram.end) : '';
            document.getElementById('liveBadge').style.display = s.mode === 'Live' ? 'inline' : 'none';
            currentChannelId = s.channel?.id || '';
            currentVolume = s.volume || 0;
            isMuted = s.muted || false;
            updateVolumeUI();
            updateChannelActive();
            if (currentChannelId) loadEpg(currentChannelId);
            document.getElementById('currentTime').textContent = new Date().toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
        }

        function renderChannels(data) {
            const grid = document.getElementById('channelGrid');
            grid.innerHTML = '';
            channelList = [];
            if (data.favorites?.length) {
                data.favorites.forEach(c => { channelList.push(c); addChannelItem(grid, c, true); });
            }
            if (data.groups?.length) {
                data.groups.forEach(g => {
                    if (g.name === '我的收藏') return;
                    g.channels?.forEach(c => { channelList.push(c); addChannelItem(grid, c); });
                });
            }
            updateChannelActive();
        }
        function addChannelItem(grid, c, isFavorite) {
            const div = document.createElement('div');
            div.className = 'channel-item';
            div.dataset.id = c.id;
            const logo = c.logo;
            if (logo && logo.startsWith('http')) {
                div.innerHTML = '<div class=logo><img src=' + logo + ' style=width:28px;height:28px;object-fit:contain></div><div class=name>' + c.name + '</div>';
            } else {
                const icon = isFavorite ? 'star' : 'tv';
                div.innerHTML = '<div class=logo><span style=font-size:22px>' + icon + '</span></div><div class=name>' + c.name + '</div>';
            }
            div.onclick = () => changeChannel(c.id);
            grid.appendChild(div);
        }
        function renderEpg(data) {
            const list = document.getElementById('epgList');
            const channelName = document.getElementById('epgChannelName');
            if (!data.programs?.length) { list.innerHTML = '<div class=""loading"">暂无节目信息</div>'; return; }
            const channel = channelList.find(c => c.id === data.channelId);
            channelName.textContent = channel?.name || '当前频道';
            list.innerHTML = '';
            const now = new Date();
            data.programs.forEach((p, i) => {
                const div = document.createElement('div');
                div.className = 'epg-item' + (p.isCurrent ? ' current' : '');
                const isPast = p.end && new Date('2000/1/1 ' + p.end) < now;
                if (isPast) div.classList.add('past');
                div.innerHTML = '<div class=""epg-time""><span>' + p.start + '</span><span class=""epg-time-end"">' + p.end + '</span></div>' +
                    '<div class=""epg-content""><div class=""epg-name"">' + p.name + (p.isCurrent ? '<span class=""epg-badge epg-badge-current"">正在播出</span>' : (i === 1 ? '<span class=""epg-badge epg-badge-next"">下一节目</span>' : '')) + '</div></div>';
                div.onclick = () => changeChannel(data.channelId);
                list.appendChild(div);
            });
        }

        function updateVolumeUI() {
            document.getElementById('volFill').style.width = (isMuted ? 0 : currentVolume) + '%';
            document.getElementById('volValue').textContent = isMuted ? '🔇' : currentVolume + '%';
            document.getElementById('volIcon').textContent = isMuted ? '🔇' : (currentVolume > 50 ? '🔊' : '🔉');
        }

        function updateChannelActive() {
            document.querySelectorAll('.channel-item').forEach(el => el.classList.toggle('active', el.dataset.id === currentChannelId));
        }

        async function play() { send('play'); await loadStatus(); }
        async function pause() { send('pause'); await loadStatus(); }
        async function stop() { send('stop'); await loadStatus(); }
        async function toggleMute() { send('volume', { volume: isMuted ? currentVolume : 0 }); await loadStatus(); }
        async function setVolumeFromClick(e) {
            const bar = document.getElementById('volBar');
            const pct = Math.round((e.clientX - bar.getBoundingClientRect().left) / bar.offsetWidth * 100);
            send('volume', { volume: pct }); await loadStatus();
        }
        async function changeChannel(id) { send('channel', { channelId: id }); await loadStatus(); }
        async function prevChannel() { var idx = channelList.findIndex(c => c.id === currentChannelId); if (idx > 0) await changeChannel(channelList[idx - 1].id); }
        async function nextChannel() { var idx = channelList.findIndex(c => c.id === currentChannelId); if (idx >= 0 && idx < channelList.length - 1) await changeChannel(channelList[idx + 1].id); }
        function fullscreen() { send('fullscreen'); }
	        function switchSource() { send('switchSource'); }
        async function exitApp() { if (confirm('确定要关闭播放器吗？')) send('exit'); }

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
    }

    public class WebRemoteChannel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Logo { get; set; }
    }

    public class WebRemoteProgram
    {
        public string Name { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public bool IsCurrent { get; set; }
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
}
