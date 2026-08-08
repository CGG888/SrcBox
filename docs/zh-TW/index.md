---
layout: home

hero:
  name: "源匣 (SrcBox)"
  text: "高效能 Windows IPTV 播放器"
  tagline: "基於 libmpv 核心，支援 EPG、時移回看、Web 遠程控制、極速切台的現代化 IPTV 播放工具。"
  image:
    src: /logo.svg
    alt: 源匣 Logo
  actions:
    - theme: brand
      text: 開始使用
      link: /zh-TW/guide/
    - theme: alt
      text: 訪問 GitHub
      link: https://github.com/CGG888/SrcBox

features:
  - title: FCC 極速切台
    details: 毫秒級快速切台，告別傳統 IPTV 的漫長等待
    icon: ⚡
  - title: M3U + EPG
    details: 支援本地/遠程 M3U 播放列表，完整 XMLTV 電子節目單
    icon: 📺
  - title: 時移與回看
    details: 即時拖動回看直播歷史，模板自動生成回放連結
    icon: ⏪
  - title: 預約錄製
    details: 前台/後台雙模式錄製，支援 WebDAV 雲端同步
    icon: 🎬
  - title: 多屏播放
    details: 支援 4/6/9 屏幕同時觀看，數字鍵快速切換
    icon: 🖥️
  - title: Web 遠程控制
    details: 瀏覽器直接操控播放器，隨時隨地掌控電視
    icon: 📱
  - title: 硬體解碼
    details: D3D11VA/DXVA2/NVDEC/軟體多模式自動切換
    icon: 🔧
  - title: 頻道管理
    details: 分組、搜尋、收藏、歷史，靈活的列表管理
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
  <strong>免責聲明：</strong> 本頁面展示的所有影片、截圖及演示畫面僅作功能展示用途，並非實際可播放或可用的媒體資源。<strong>本專案不提供任何 m3u 播放清單檔案及其中包含的頻道數據，亦不對第三方數據源負責。</strong>
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
