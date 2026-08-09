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
| 📺 **M3U + EPG** | Local/remote M3U playlists, full XMLTV support |
| ⏪ **Timeshift & Catchup** | Real-time seeking in live streams |
| 🎬 **Scheduled Recording** | Foreground/background modes, WebDAV sync |
| 🖥️ **Multi-Screen** | 4/6/9 screen simultaneous viewing |
| 📱 **Web Remote** | Control player from browser, anywhere |
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

## Recent Updates (v1.1.9)

- 🖥️ **Multi-Screen** - 4/6/9 screen viewing, 1-9 keys quick select
- 🎬 **Scheduled Recording** - Foreground/background modes, toast notification
- 🔧 **Decoder Selection** - Auto/D3D11VA/DXVA2/NVDEC/Software
- ⌨️ **Shortcuts Help** - New ShortcutsWindow for keyboard reference
- ⚡ **Timeshift Enhancement** - Seek limited within program boundaries

[View All Updates →](https://github.com/CGG888/SrcBox/releases)

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

---

## License

This project is open source under the [MIT License](./LICENSE.txt).

## Acknowledgments

- [libmpv](https://mpv.io/) - Powerful cross-platform media playback engine
- [ModernWpf](https://github.com/AngelSoyoso/ModernWpf) - Modern WPF UI library
- [VitePress](https://vitepress.dev/) - Static documentation site generator
