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
  - title: FCC Fast Zapping
    details: Millisecond-level channel switching, no more waiting
    icon: ⚡
  - title: M3U + EPG
    details: Local/remote M3U playlists, full XMLTV program guide
    icon: 📺
  - title: Timeshift & Catchup
    details: Real-time seeking in live streams, template-based catchup
    icon: ⏪
  - title: Scheduled Recording
    details: Foreground/background modes, WebDAV cloud sync
    icon: 🎬
  - title: Multi-Screen
    details: 4/6/9 screen simultaneous viewing, number keys quick switch
    icon: 🖥️
  - title: Web Remote
    details: Control player from browser, anywhere access
    icon: 📱
  - title: Hardware Decoding
    details: D3D11VA/DXVA2/NVDEC/software auto-switch
    icon: 🔧
  - title: Channel Management
    details: Groups, search, favorites, history - flexible list control
    icon: 📋
---

<ClientOnly>
  <VideoShowcase />
</ClientOnly>

<ClientOnly>
  <ScreenshotGallery />
</ClientOnly>

<ClientOnly>
  <TechStack />
</ClientOnly>

<ClientOnly>
  <HelpSection />
</ClientOnly>

<div class="disclaimer">
  <strong>Disclaimer:</strong> The videos, screenshots, and demos shown are for functional demonstration only and are not actual playable media resources. <strong>This project does not provide any m3u playlist files or channel data.</strong>
</div>

<style>
/* Hide volume controls for WebKit browsers */
video::-webkit-media-controls-volume-slider,
video::-webkit-media-controls-mute-button,
video::-webkit-media-controls-volume-control-hover-background,
video::-webkit-media-controls-volume-panel {
  display: none !important;
}

.disclaimer {
  max-width: 800px;
  margin: 60px auto;
  padding: 20px;
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-border);
  border-radius: 12px;
  font-size: 0.9rem;
  color: var(--vp-c-text-2);
  text-align: center;
}
</style>
