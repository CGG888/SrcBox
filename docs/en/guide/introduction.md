# Introduction

**SrcBox** is a high-performance, modern IPTV player designed for Windows.

## Overview

SrcBox is built on the powerful **libmpv** playback engine, combined with **WPF**'s modern UI design, providing users with a smooth and stable live TV viewing experience. It supports M3U playlists, EPG electronic program guides, catchup, timeshift, recording and other core features, with deep optimization for IPTV scenarios.

## Technical Architecture

| Component | Technology |
|-----------|------------|
| Playback Engine | libmpv |
| UI Framework | WPF + ModernWpf |
| Language | C# (.NET 8) |
| Recording Sync | WebDAV |
| Remote Control | WebSocket + HTTP |

## Main Features

### Core Playback
- M3U playlist parsing (local/remote)
- EPG electronic program guide (XMLTV format)
- Live timeshift and catchup replay
- Hardware acceleration support

### Special Features
- **FCC Fast Channel Change**: Millisecond-level ultra-fast channel switching
- **Web Remote Control**: Remote control via browser
- **Audio Optimization**: Volume gain, max volume, audio delay
- **Deinterlace Processing**: Optimized for interlaced video
- **WebDAV Recording**: Local recording with cloud sync

### User Experience
- Dark/Light theme adaptation
- Multi-language UI (Simplified/Traditional Chinese, English, Russian)
- Channel favorites and history
- Program reminder notifications

## Use Cases

- Watch IPTV live TV
- Record favorite program content
- Catch up on missed TV shows
- Remote player control

## System Requirements

- Windows 10/11
- .NET 8 Runtime
- Network connection (for live streaming and EPG)

## Open Source License

This project is open source under the MIT license. You are free to use, modify, and distribute it.
