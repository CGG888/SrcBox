# FAQ

## Playback Issues

**Q: Video plays but no sound, what to do?**
A: Some IPTV streams have slow audio probing. Try:
- Switch audio tracks (if multiple tracks available)
- Restart playback
- Check system volume settings

**Q: Channel switching is slow, how to improve?**
A: Ensure FCC Fast Channel Switching is enabled (Settings > Playback > FCC). Also try:
- Enable hardware decoding (D3D11VA)
- Check network latency
- Disable UDP multicast optimization (if not needed)

**Q: Playback stuttering/buffering, what to do?**
A: Try these methods:
- Switch decoder (Auto/D3D11VA/DXVA2/NVDEC/Software)
- Check network stability
- Lower video quality expectations (some sources have limitations)
- Try auto source switching

**Q: How to show control bar in fullscreen?**
A: Move mouse to bottom of screen to show floating control bar. Auto-hide time can be adjusted in settings.

## M3U and Playlists

**Q: How to load M3U playlist?**
A: Click top menu "Open > Open M3U File" or "Open > Open M3U URL", supports local files and network addresses.

**Q: What M3U formats are supported?**
A: Supports standard M3U/M3U8 format, compatible with UTF-8 and GB18030 encoding, supports `#EXTINF` extended attributes (like logo, group info).

**Q: Can I use TXT format channel list?**
A: Yes, TXT format has one URL per line, the program will auto-detect.

**Q: Is M3U cache permanent?**
A: No, M3U cache defaults to 12 hours (adjustable via `M3uCacheTtlHours` in settings). You can force refresh via "Manage M3U Lists".

## EPG Electronic Program Guide

**Q: EPG shows "No Data", what to do?**
A: Please check:
- XMLTV URL is accessible
- It's in GZIP format (.gz)
- `tvg-id` matches the channel in M3U
- Network can access the EPG data source

**Q: How to configure EPG?**
A: Enter XMLTV URL in Settings window's "EPG" tab, supports local files and network addresses.

**Q: Program guide time is incorrect?**
A: Check if system timezone is correct, EPG data relies on local time for schedule calculation.

## Recording

**Q: How to start recording?**
A: Press `R` key or click record button to start, press again to stop.

**Q: Where are recorded files saved?**
A: Default save location is "Documents\SrcBox\Recordings", can be changed in settings.

**Q: Does it support WebDAV upload?**
A: Yes. Configure WebDAV server address, username and password in settings, recordings will auto-upload after completion.

**Q: What's the difference between foreground and background recording?**
A:
- **Foreground recording**: Screen jumps to recording channel, shows recording timer
- **Background recording**: Keeps current playback channel, no jump, shows Toast notification when complete

## Timeshift & Catchup

**Q: How to use timeshift?**
A: Press `T` key to enter timeshift mode, drag progress bar to rewind live. Press `T` again to return to live.

**Q: How to use Catchup?**
A: Requires channel to support `catchup-source` attribute. Click on past program in EPG to watch.

**Q: How far back can timeshift go?**
A: Depends on channel server's `catchup-source` configuration and timeshift buffer settings.

## Multi-Screen

**Q: How to enable multi-screen mode?**
A: Press `Ctrl+4`/`Ctrl+6`/`Ctrl+9` to open 4/6/9 screen mode respectively.

**Q: How to switch channels in multi-screen mode?**
A: Use `1-9` number keys to select screen, `↑/↓` to switch channels, `←/→` to switch sources.

## Web Remote Control

**Q: How to enable Web Remote Control?**
A: Enable Web Remote Control in settings, follow prompts to set port and password (optional).

**Q: How to access Web Remote Control?**
A: Enter `http://playerPC_IP:port` in a browser on the same local network.

## Keyboard Shortcuts

**Q: What are the common shortcuts?**
A:
| Shortcut | Function |
|----------|----------|
| `Space` | Play/Pause |
| `S` | Stop |
| `↑/↓` | Previous/Next Channel |
| `M` | Mute |
| `Enter` | Toggle Fullscreen |
| `L` | Channel List |
| `E` | EPG Program Guide |
| `R` | Start/Stop Recording |
| `T` | Timeshift Mode |
| `Ctrl+,` | Settings |
| `Ctrl+/` | Shortcuts Help |

## Troubleshooting

**Q: App crashes on startup?**
A: Ensure `libmpv-2.dll` is in the program execution directory.

**Q: Settings not saving?**
A: Ensure program directory has write permissions, or try running as administrator.

**Q: How to report issues or suggestions?**
A: Please submit at [GitHub Issues](https://github.com/CGG888/SrcBox/issues), we will respond as soon as possible.

## System Requirements

**Q: Which operating systems are supported?**
A: Windows 10/11 (x64).

**Q: What runtime is required?**
A: .NET 8 Runtime. If prompted for missing runtime on first run, please download and install from Microsoft official site.
