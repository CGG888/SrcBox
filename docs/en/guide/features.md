# Features

## Playback Control

- **Playback Control**: Play/Pause, Stop, Fast Forward/Rewind, Seek
- **Volume Control**: Slider adjustment + Mute (0-100)
- **Audio Settings**: Volume Gain (-200dB~+60dB), Max Volume (100%~1000%), Audio Delay (-100s~+100s)
- **Status Indicators**: Live/Replay/Timeshift status displayed in overlay
- **Speed Control**: Timeshift/Replay mode supports 0.5×~5.0× speed with pitch correction

## Fast Channel Change (FCC)

IPTV-optimized fast channel switching technology, millisecond-level channel changes, goodbye to long waits.

<video controls width="100%">
  <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
</video>

## Program Guide & Replay

### M3U Parsing
Supports local/remote M3U playlists, compatible with UTF-8/GB18030 encoding, supports `#EXTINF` extended attributes.

### EPG Electronic Program Guide
Supports XMLTV (gz) format, automatic day switching, smart `tvg-id` matching.
[View configuration guide](./epg)

### Catchup
Template-based catchup URL generation (e.g., `{utc:yyyyMMddHHmmss}`), never miss精彩的节目.
[View usage guide](./catchup-timeshift)

<video controls width="100%">
  <source src="/screenshots/catchup.mp4" type="video/mp4">
</video>

### Timeshift
Real-time seeking through live stream history, fast forward/rewind limited within program boundaries.
[View usage guide](./catchup-timeshift)

<video controls width="100%">
  <source src="/screenshots/timeshift.mp4" type="video/mp4">
</video>

## Multi-Screen Playback

Supports 4/6/9 screen simultaneous playback, ideal for monitoring or multitasking scenarios.

| Shortcut | Function |
|----------|----------|
| `Ctrl+4/6/9` | Open corresponding multi-screen mode |
| `1-9` | Select corresponding screen |
| `↑/↓` | Switch channel |
| `←/→` | Switch source |

## Scheduled Recording

Supports front/back scheduled recording modes with automatic Toast notification on completion.

- **Front Recording**: Screen jumps to recording channel, supports custom duration auto-stop
- **Back Recording**: Keeps current playback channel, supports scheduled stop
- **WebDAV Sync**: Auto-upload recordings to WebDAV server after completion

## Web Remote Control

Remotely control the player through a browser, control your TV from anywhere.

- Playback control, volume adjustment, channel switching
- Real-time status view, program info display
- Optional password protection

[View detailed guide](./web-remote)

## Channel Management

- **Groups**: Channel group management with sorting
- **Search**: Quick channel search
- **Favorites**: Bookmark favorite channels
- **History**: Playback history, persisted locally
- **R/T Badges**: Quickly identify channels supporting Catchup(R) or Timeshift(T)

## Channel Preview

Hover over channels in the list to preview the current画面, requires rtp2httpd with `X-Request-Snapshot`.

[View detailed guide](./channel-preview)

## Decoder Selection

Supports multiple hardware/software decoders, dynamically switchable during playback:

| Decoder | Description |
|---------|------------|
| Auto | Automatically select best decoder |
| D3D11VA | Windows Video Acceleration (default) |
| DXVA2 | DirectX Video Acceleration |
| NVDEC | NVIDIA GPU decoding |
| Software | CPU software decoding |

## Video Optimization

### Deinterlace
Optimized for 1080i/720i interlaced video streams:
- Three modes: Auto / Force On / Off
- Two algorithms: yadif (default) / bwdif (better quality)
- Field settings: Auto / Top Field First (TFF) / Bottom Field First (BFF)

## Fullscreen Interaction

- **Double-click fullscreen** or press `Enter` to enter fullscreen
- **Floating control bar**: Auto-shows on mouse move to bottom
- **Side drawer**: Mouse near edge shows channel list (right) or EPG (left)
- **ESC** to exit fullscreen

## UI Features

- **Dark/Light Theme**: Perfect adaptation for Windows 10/11
- **Minimal Mode**: Compact window form, suitable for small screen viewing
- **Multi-language**: Supports Simplified Chinese, Traditional Chinese, English, Russian

## Keyboard Shortcuts

| Shortcut | Function |
|----------|----------|
| `Space` | Play/Pause |
| `S` | Stop |
| `↑/↓` | Previous/Next channel |
| `←/→` | Previous/Next source (Live) / Rewind/Fast Forward (Timeshift) |
| `M` | Mute |
| `Enter` | Toggle fullscreen |
| `L` | Channel list |
| `E` | EPG Program Guide |
| `R` | Start/Stop recording |
| `T` | Timeshift mode |
| `Ctrl+,` | Settings |
| `Ctrl+/` | Shortcuts help |

[View complete shortcuts guide](./keyboard-shortcuts)
