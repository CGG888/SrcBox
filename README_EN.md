# SrcBox (Windows / WPF)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)](https://www.microsoft.com/windows)
[![Version](https://img.shields.io/github/v/release/CGG888/SrcBox?display_name=tag&color=58a6ff)](https://github.com/CGG888/SrcBox/releases/latest)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](http://makeapullrequest.com)

[English](./README_EN.md) | [中文](./README.md)

**SrcBox** is a high-performance, modern IPTV player for Windows. Built on **libmpv**, combined with **WPF** UI, supporting M3U playlists, EPG, timeshift, scheduled recording, multi-screen playback, web remote control, and more.

[⬇️ Download Latest](https://github.com/CGG888/SrcBox/releases) | [📖 Documentation](https://srcbox.top/en) | [🐛 Report Issues](https://github.com/CGG888/SrcBox/issues)

---

> **Disclaimer**:
> All videos, screenshots, and demos on this page are for **functional demonstration only** and are not actual playable or available media resources. **This project does not provide any m3u playlist files or channel data**, and is not responsible for third-party data sources. Please use legal playback sources and comply with local laws.

## Features

| Feature | Description |
|:---:|------|
| ⚡ **FCC Fast Zapping** | Millisecond-level channel switching |
| 📸 **Channel Preview** | Hover to preview channels, requires rtp2httpd |
| 📺 **M3U + EPG** | Local/remote M3U playlists, full XMLTV support |
| ⏪ **Timeshift & Catchup** | Real-time seeking in live streams |
| 🎬 **Scheduled Recording** | Foreground/background modes, WebDAV sync |
| 🖥️ **Multi-Screen** | 4/6/9 screen simultaneous viewing |
| 📱 **Web Remote** | Scan and control the player from your phone: standard remote pad + full interface |
| 🔒 **HTTPS & PWA** | One port serves both http and https, built-in certificate, installable to the home screen |
| 🔧 **Hardware Decoding** | D3D11VA/DXVA2/NVDEC/Software auto-switch |
| 📋 **Channel Management** | Groups, search, favorites, history |

## System Requirements & Shortcuts

| System Requirements | |
|:---:|------|
| **OS** | Windows 10 / 11 (x64) |
| **Runtime** | .NET 8.0 SDK |
| **Dependency** | libmpv-2.dll (included in repo) |

| Common Shortcuts | |
|:---:|------|
| `Space` | Play/Pause |
| `Enter` | Toggle Fullscreen |
| `↑↓` | Switch Channel |
| `E` | EPG Guide |
| `L` | Channel List |
| `R` | Start/Stop Recording |

## Web Remote (Phone Remote)

Once enabled, scan the QR code in Settings and the remote opens in your phone browser — no app to install:

- **Two layouts**: a standard remote pad (D-pad, number keys, volume, source switch/mute, fullscreen) and the full interface (status, playback controls, channel list, EPG, sources, reminders and recordings); the choice is remembered
- **HTTP + HTTPS on one port**: the same port serves both `http://` and `https://`; the built-in self-signed certificate can be downloaded from Settings or the remote page (install it as a CA certificate on Android, install the profile and enable full trust on iPhone)
- **Install to the home screen (PWA)**: "Install app" on Android Chrome/Edge, or Share → "Add to Home Screen" on iOS Safari; it then runs full screen in its own window
- **Tuning feedback**: when you switch channels from the phone, the player shows the same channel number you entered

See the [Web Remote documentation](https://srcbox.top/en/guide/web-remote) for the full steps.

## Recent Updates (v1.1.15)

- 🖥️ **Fullscreen rework** - the main window goes fullscreen itself, overlays are owned by it; window geometry and caption buttons are restored on exit
- 📱 **Phone remote without an app** - single-port HTTP + HTTPS, downloadable self-signed certificate, installable to the home screen (PWA)
- 🎨 **Consistent fullscreen look** - light theme uses the dark top/bottom bars and panels in fullscreen; panels take 72% of the available height, vertically centred
- ⚡ **Progressive EPG** - the programme list appears per source instead of waiting for every source to finish
- 🧠 **Memory** - remembers the last channel per M3U source and the last source per channel (switchable in Settings)
- 🔊 **Unity volume** - default volume is 1.0, and the max-volume setting can boost above 1.0

[v1.1.15 release notes →](https://srcbox.top/releases/v1.1.15) | [View All Updates →](https://github.com/CGG888/SrcBox/releases)

## Architecture

| Layer | Description |
|------|-------------|
| **UI Layer** | WPF (ModernWpf) modern interaction |
| **Architecture** | `Architecture/` (Application/Platform/Presentation) |
| **Interop** | `MpvPlayer.cs` + `MpvPlayerEngineAdapter` wrapping libmpv |
| **Services** | `M3UParser`, `EpgService`, `WebDAV`, etc. |

## Quick Start

```powershell
# Build
dotnet build

# Run
dotnet run

# Test
dotnet test .\Tests\LibmpvIptvClient.Tests.csproj
```

## Documentation

| Language | Link |
|----------|------|
| 简体中文 | [srcbox.top](https://srcbox.top) |
| 繁體中文 | [srcbox.top/zh-TW](https://srcbox.top/zh-TW) |
| English | [srcbox.top/en](https://srcbox.top/en) |
| Русский | [srcbox.top/ru](https://srcbox.top/ru) |

## Privacy & Security

- All configs, playlists, and EPG caches are stored locally on your device
- Network requests are only made when fetching M3U/EPG URLs or checking for updates

## Screenshots

- Main Interface  
  ![main](docs/screenshots/main.png)

- Multi-Screen  
  ![multi-screen](docs/screenshots/multiscreen.png)

- Settings  
  ![settings](docs/screenshots/settings.png)

- Web Remote (standard pad · dark / full interface · dark)  
  ![remote-pad-dark](docs/screenshots/web-remote-pad-dark.png)
  ![remote-full-dark](docs/screenshots/web-remote-full-dark.png)

---

## License

This project is open source under the [MIT License](./LICENSE.txt). You are free to use, modify, and distribute it, provided the original copyright notice is retained.

## Acknowledgments

- [libmpv](https://mpv.io/) - Powerful cross-platform media playback engine
- [ModernWpf](https://github.com/AngelSoyoso/ModernWpf) - Modern WPF UI library
- [VitePress](https://vitepress.dev/) - Static documentation site generator
- [Downloader](https://github.com/avgp/ngx-downloader) - Segment download library
