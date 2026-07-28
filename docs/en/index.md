---
layout: home

hero:
  name: "SrcBox"
  text: "High Performance Windows IPTV Player"
  tagline: "Based on libmpv, supporting EPG, timeshift, Web remote control, and ultra-fast channel switching."
  image:
    src: /logo.svg
    alt: SrcBox Logo
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: GitHub
      link: https://github.com/CGG888/SrcBox

features:
  - title: Modern UI
    details: Built with WPF/ModernWpf, perfect dark/light theme adaptation for Windows 10/11.
    icon: 🎨
  - title: High Performance
    details: Powered by libmpv, hardware decoding (d3d11va), low resource usage, millisecond-level channel switching.
    icon: 🚀
  - title: Fast Zapping (FCC)
    details: Optimized for IPTV with FCC technology, delivering seamless channel switching.
    icon: ⚡
  - title: Smart EPG
    details: Full XMLTV (gz) support with automatic day switching and intelligent program matching.
    icon: 📅
  - title: Timeshift & Catchup
    details: Real-time seeking in live streams and auto-generated catchup replay URLs.
    icon: ⏪
  - title: Scheduling & Alerts
    details: Program reminders with remind-only or auto-play actions, batch management.
    icon: ⏰
  - title: Recording & Upload
    details: Local recording, recording index, and WebDAV upload queue.
    icon: ⬆️
  - title: Web Remote Control
    details: Control the player through browser, playback, volume, channel switching.
    icon: 📱
  - title: Audio Settings
    details: Volume gain, max volume limit, and audio delay adjustment.
    icon: 🔊
  - title: Deinterlace
    details: Optimized for 1080i/720i interlaced video with auto-detection.
    icon: 🖼️
  - title: Channel Management
    details: Grouping, search, favorites, and history with local persistence.
    icon: 📺
  - title: Multi-language
    details: Support for Simplified/Traditional Chinese, English, Russian.
    icon: 🌍
---

<script setup>
import { onMounted } from 'vue'

onMounted(() => {
  // Custom logic
})
</script>

<style>
/* Hide volume controls for WebKit browsers (Chrome, Edge, Safari) */
video::-webkit-media-controls-volume-slider,
video::-webkit-media-controls-mute-button,
video::-webkit-media-controls-volume-control-hover-background,
video::-webkit-media-controls-volume-panel {
  display: none !important;
}
</style>

## Overview

**SrcBox** is a high-performance, modern IPTV player designed for the Windows platform.

Built on the powerful **libmpv** playback engine and combined with **WPF**'s modern UI design, it delivers a smooth and stable live viewing experience. It supports M3U playlists, EPG, catchup, timeshift, recording and more, with deep optimization for IPTV scenarios (FCC fast channel switching, UDP multicast).

### Core Features

- **libmpv Engine**: High performance, low resource, hardware acceleration
- **FCC Fast Zapping**: Millisecond-level channel switching optimized for IPTV
- **EPG**: Full XMLTV support with intelligent matching
- **Timeshift & Catchup**: Live seeking and template-based catchup
- **Web Remote**: Browser-based player control
- **Audio Optimization**: Volume gain, max volume, audio delay
- **Deinterlace**: Smart processing for interlaced video
- **WebDAV Recording**: Local recording with cloud sync
- **Multi-language**: Chinese (Simplified/Traditional), English, Russian

## Feature Demos

<div style="display: flex; flex-direction: column; gap: 60px; align-items: center; padding-bottom: 40px;">
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Fast Zapping (FCC)</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Millisecond-level ultra-fast channel switching</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Catchup / Replay</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Auto-generated catchup URLs from templates</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/catchup.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Timeshift</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Real-time seeking back in live streams</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/timeshift.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
</div>

## Screenshots

<div style="display: flex; gap: 20px; overflow-x: auto; padding-bottom: 20px; justify-content: start;">
  <img src="/screenshots/main.png" alt="Main Interface" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/fullscreen-overlay.png" alt="Fullscreen Overlay" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/settings.png" alt="Settings" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
</div>

## Tech Stack

| Component | Technology |
|-----------|------------|
| Playback Engine | libmpv |
| UI Framework | WPF + ModernWpf |
| Language | C# (.NET 8) |
| Recording Sync | WebDAV |
| Remote Control | WebSocket + HTTP |

## Get Help

- 📖 [User Guide](/guide/) - Detailed features and configuration
- ⌨️ [Keyboard Shortcuts](/guide/keyboard-shortcuts) - Quick operation guide
- 🐛 [Report Issues](https://github.com/CGG888/SrcBox/issues) - Bug reports and suggestions
- 📦 [Downloads](https://github.com/CGG888/SrcBox/releases) - Get the latest version

>> **[Visit Official Documentation Website](https://srcbox.top/en)** <<

<div style="margin-top: 40px; padding-top: 20px; border-top: 1px solid var(--vp-c-divider); font-size: 14px; color: var(--vp-c-text-2);">
  <p><strong>Disclaimer:</strong> The videos, screenshots, and demos shown are for functional demonstration only and are not actual playable media resources. <strong>This project does not provide any m3u playlist files or channel data.</strong></p>
</div>
