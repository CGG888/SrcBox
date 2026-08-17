# Roadmap

We are committed to continuously improving the IPTV viewing experience.

## <span style="font-size:1.2em">✨</span> Completed

### Core Playback

<span style="color:#22c55e">✔</span> **FCC Fast Channel Switching** - Millisecond-level switching, optimized for IPTV<br>
<span style="color:#22c55e">✔</span> **M3U Playlists** - Local/remote support, UTF-8/GB18030 encoding, `#EXTINF` extended attributes<br>
<span style="color:#22c55e">✔</span> **M3U Binary Cache** - ETag/Last-Modified validation, millisecond-level loading<br>
<span style="color:#22c55e">✔</span> **EPG Electronic Program Guide** - XMLTV (gz) parsing, CCTV/Education channel type suffix<br>
<span style="color:#22c55e">✔</span> **Catchup Replay** - Template-based automatic catchup URL generation, timeshift replay<br>
<span style="color:#22c55e">✔</span> **Timeshift** - Real-time seeking in live streams, seek within program boundaries<br>
<span style="color:#22c55e">✔</span> **Channel Management** - Groups, search, favorites, history, group sorting<br>
<span style="color:#22c55e">✔</span> **RTP Direct Address** - UDP optimization, reduced network latency

### Playback Optimization

<span style="color:#22c55e">✔</span> **Hardware Decoding** - D3D11VA/DXVA2/NVDEC/Software auto-switch<br>
<span style="color:#22c55e">✔</span> **Deinterlace** - 1080i/720i optimization, yadif/bwdif algorithms<br>
<span style="color:#22c55e">✔</span> **Audio Settings** - Volume gain, max volume, audio delay<br>
<span style="color:#22c55e">✔</span> **Speed Control** - Timeshift/catchup mode 0.5×~5.0× with pitch correction<br>
<span style="color:#22c55e">✔</span> **Auto Source Switching** - Automatic switch when source fails<br>
<span style="color:#22c55e">✔</span> **Connection Preheating** - Pre-establish connections, speed up channel switching

### Multi-Screen & Recording

<span style="color:#22c55e">✔</span> **Multi-Screen Playback** - 4/6/9 screen simultaneous viewing, number key selection<br>
<span style="color:#22c55e">✔</span> **Local Recording** - Direct recording to local disk<br>
<span style="color:#22c55e">✔</span> **WebDAV Upload** - Automatic upload after recording<br>
<span style="color:#22c55e">✔</span> **Scheduled Recording** - Foreground/background modes, timed auto-stop

### Interface & Interaction

<span style="color:#22c55e">✔</span> **Dark/Light Theme** - Perfect adaptation for Windows 10/11<br>
<span style="color:#22c55e">✔</span> **Fullscreen Overlay** - Mouse-triggered control bar at bottom<br>
<span style="color:#22c55e">✔</span> **Side Drawers** - Channel list (right) and EPG (left)<br>
<span style="color:#22c55e">✔</span> **Compact Mode** - Compact window layout<br>
<span style="color:#22c55e">✔</span> **System Tray** - Persistent icon with quick access menu<br>
<span style="color:#22c55e">✔</span> **Keyboard Shortcuts** - Complete shortcuts support with help window<br>
<span style="color:#22c55e">✔</span> **Close Mode Memory** - Remember exit/minimize to tray choice<br>
<span style="color:#22c55e">✔</span> **Channel Preview** - Hover to show channel thumbnail, customizable size<br>
<span style="color:#22c55e">✔</span> **Debug Window** - Real-time log viewing, debug mode toggle

### Remote & Sync

<span style="color:#22c55e">✔</span> **Web Remote Control** - Browser-based player control, full playback, replay, reminders, recording<br>
<span style="color:#22c55e">✔</span> **Program Reminders** - Scheduled notifications with auto-play option<br>
<span style="color:#22c55e">✔</span> **Multi-language** - Simplified Chinese, Traditional Chinese, English, Русский

### Source Health & Stability

<span style="color:#22c55e">✔</span> **Source Health Detection** - Background HTTP HEAD probing, real-time status display<br>
<span style="color:#22c55e">✔</span> **Source Status Indicator** - Channel list shows source health (green/red ellipse)<br>
<span style="color:#22c55e">✔</span> **Right-click Source Menu** - View all sources health status, latency, switch source<br>
<span style="color:#22c55e">✔</span> **Auto Source Degradation** - Automatically switch to healthy backup when primary fails

## <span style="font-size:1.2em">🚧</span> In Progress

<span style="color:#f59e0b">⚙</span> **EPG Status Chip Clickable** - Click to return to live, UX evaluation<br>
<span style="color:#f59e0b">⚙</span> **Reminder Notification Animations** - Fade-in/slide-in effects designed

## <span style="font-size:1.2em">📌</span> Future Plans

<span style="color:#6b7280">○</span> **Cloud Recording (PVR)** - Record to remote storage<br>
<span style="color:#6b7280">○</span> **Playback Chain Optimization** - Continue reducing channel switch latency, optimize weak network stability<br>
<span style="color:#6b7280">○</span> **Test Coverage Expansion** - Unit tests for playback state machine, recording index, EPG sync<br>
<span style="color:#6b7280">○</span> **Recording Experience Enhancement** - Recording status sync, remote metadata consistency
