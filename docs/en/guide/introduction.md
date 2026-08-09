# Introduction

**SrcBox** is a high-performance, modern IPTV player designed exclusively for Windows. Built on the powerful **libmpv** playback engine with **WPF**'s modern UI design, it delivers a smooth and stable live TV viewing experience.

## Core Features

### FCC Fast Channel Switching
Optimized for IPTV scenarios with millisecond-level channel switching, eliminating long waits.

### M3U + EPG
- Local/remote M3U playlist support
- Full XMLTV (gz) electronic program guide
- Automatic day switching, intelligent program matching

### Timeshift & Catchup
- Real-time seeking in live streams
- Template-based catchup URL generation
- Seek limited within program boundaries

### Multi-Screen
4/6/9 screen simultaneous viewing, number keys for quick switching, ideal for monitoring or multitasking.

### Scheduled Recording
- Foreground/background recording modes
- Automatic timed stop
- WebDAV cloud sync

### Web Remote Control
Control the player from any browser, supporting playback control, volume adjustment, and channel switching.

## Technical Architecture

| Layer | Component | Description |
|-------|-----------|-------------|
| Playback Engine | libmpv-2.dll | High-performance cross-platform player |
| Rendering | WindowsFormsHost | Embed mpv window handle in WPF |
| UI Framework | WPF + ModernWpf | Modern Windows interface |
| Services | M3U/EPG/Recording | Core business services |

| Technology | Description |
|------------|-------------|
| Language | C# (.NET 8) |
| UI Framework | WPF + ModernWpf |
| Playback Engine | libmpv |
| Hardware Decode | D3D11VA / DXVA2 / NVDEC |
| Remote Control | WebSocket + HTTP |
| Recording Sync | WebDAV |
| Documentation | VitePress |

## Feature Overview

### Playback
- Play/Pause/Stop/Fast Forward/Rewind
- Volume control + mute
- Audio delay (-100s~+100s)
- Speed control (0.5×~5.0×)

### Channel Management
- Group management with sorting
- Quick search
- Favorites
- Playback history

### Video Optimization
- Hardware decode auto-switch
- Deinterlace (1080i/720i)
- Auto source switching

### Interface
- Dark/Light theme adaptation
- Fullscreen floating control bar
- Side drawers (channel list/EPG)
- Compact mode window

### Multi-Language
- Simplified Chinese
- Traditional Chinese
- English
- Russian

## Use Cases

| Scenario | Description |
|----------|-------------|
| Live TV | IPTV live streaming |
| Timeshift | Live history replay, catchup |
| Scheduled Recording | Timed recording, cloud upload |
| Multi-Screen | 4/6/9 screen monitoring |
| Remote Control | Browser-based player control |

## System Requirements

| Item | Requirement |
|------|-------------|
| OS | Windows 10/11 (x64) |
| Runtime | .NET 8 Runtime |
| Network | Broadband (streaming/EPG) |
| Optional | NVIDIA GPU (NVDEC) |

## Open Source License

This project is open source under the [MIT License](https://opensource.org/licenses/MIT). You are free to use, modify, and distribute.

## Acknowledgments

- [libmpv](https://mpv.io/) - Powerful cross-platform player engine
- [ModernWpf](https://github.com/AngelMunoz/ModernWpf) - Modern WPF control library
- [VitePress](https://vitepress.dev/) - Static documentation site generator
- [Downloader](https://github.com/avgp/ngx-downloader) - Segment download library
