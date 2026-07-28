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
  - title: 現代化介面
    details: 基於 WPF/ModernWpf 建構，完美適配 Windows 10/11 深色與淺色主題。
    icon: 🎨
  - title: 極致效能
    details: 採用 libmpv 播放核心，支援硬體解碼 (d3d11va)，低資源佔用，毫秒級快速切台。
    icon: 🚀
  - title: 極速切台 (FCC)
    details: 針對 IPTV 場景深度優化的快速切台技術，告別緩衝等待，享受絲滑換台體驗。
    icon: ⚡
  - title: 智慧節目單
    details: 完整支援 XMLTV (gz) 格式 EPG，支援自動按日切換和智慧節目匹配。
    icon: 📅
  - title: 時移與回看
    details: 支援直播流即時拖動時移，以及基於模板自動生成的 Catchup 回放。
    icon: ⏪
  - title: 預約與提醒
    details: 支援節目預約提醒與到點自動播放，提供預約清單和批次管理能力。
    icon: ⏰
  - title: 錄播與上傳
    details: 支援本地錄播、錄播索引與 WebDAV 上傳佇列。
    icon: ⬆️
  - title: Web 遠程控制
    details: 透過瀏覽器遠程控制播放器，支援播放控制、音量調節、頻道切換。
    icon: 📱
  - title: 音頻設定
    details: 支援音量增益、最大音量限制和音頻延遲調節。
    icon: 🔊
  - title: 去交錯處理
    details: 針對 1080i/720i 隔行掃描視頻流優化，支援自動檢測和多種去交錯算法。
    icon: 🖼️
  - title: 頻道管理
    details: 提供頻道分組、搜尋、收藏與歷史記錄，本地持久化管理播放清單。
    icon: 📺
  - title: 多語言支持
    details: 支援簡體中文、繁體中文、英文、俄文等多種語言介面。
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

## 專案概覽

**源匣 (SrcBox)** 是一款專為 Windows 平台打造的高效能、現代化 IPTV 播放器。

它基於強大的 **libmpv** 播放核心構建，結合 **WPF** 的現代化介面設計，為您帶來流暢、穩定的直播觀看體驗。不僅支援 M3U 播放清單、EPG 電子節目單，回看、時移、錄播等核心功能，還針對 IPTV 場景進行了深度優化。

### 核心特性

- **libmpv 播放核心**：高效能、低資源佔用，支援硬體加速解碼
- **FCC 快速切台**：針對 IPTV 場景優化的毫秒級極速切台
- **EPG 節目單**：完整支援 XMLTV 格式，智慧節目匹配
- **時移回看**：支援直播時移和基於模板的 Catchup 回放
- **Web 遠程控制**：透過瀏覽器遠程操控播放器
- **音頻優化**：音量增益、最大音量限制、音頻延遲調節
- **去交錯處理**：針對隔行掃描視頻流的智慧處理
- **WebDAV 錄播**：本地錄播配合雲端同步
- **多語言介面**：支援中文（簡/繁）、英文、俄文

## 功能演示

<div style="display: flex; flex-direction: column; gap: 60px; align-items: center; padding-bottom: 40px;">
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>毫秒級切台 (FCC)</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">極致優化的快速換台體驗</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
        您的瀏覽器不支援 video 標籤。
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Catchup / 節目回放</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">基於模板自動生成回看位址，不錯過精彩節目</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/catchup.mp4" type="video/mp4">
        您的瀏覽器不支援 video 標籤。
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Timeshift / 直播時移</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">即時拖動進度條，隨時回看直播歷史</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/timeshift.mp4" type="video/mp4">
        您的瀏覽器不支援 video 標籤。
      </video>
    </ClientOnly>
  </div>
</div>

## 介面預覽

<div style="display: flex; gap: 20px; overflow-x: auto; padding-bottom: 20px; justify-content: start;">
  <img src="/screenshots/main.png" alt="主介面" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/fullscreen-overlay.png" alt="全屏懸浮" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/settings.png" alt="設定" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
</div>

## 技術架構

| 組件 | 技術 |
|------|------|
| 播放核心 | libmpv |
| UI 框架 | WPF + ModernWpf |
| 語言 | C# (.NET 8) |
| 錄播同步 | WebDAV |
| 遠程控制 | WebSocket + HTTP |

## 獲取幫助

- 📖 [使用指南](/zh-TW/guide/) - 詳細的功能介紹和配置說明
- 🐛 [問題反饋](https://github.com/CGG888/SrcBox/issues) - 報告 Bug 或提出建議
- 📦 [下載安裝](https://github.com/CGG888/SrcBox/releases) - 獲取最新版本

<div style="margin-top: 40px; padding-top: 20px; border-top: 1px solid var(--vp-c-divider); font-size: 14px; color: var(--vp-c-text-2);">
  <p><strong>免責聲明：</strong> 本頁面展示的所有影片、截圖及演示畫面僅作功能展示用途，並非實際可播放或可用的媒體資源。<strong>本專案不提供任何 m3u 播放清單檔案及其中包含的頻道數據，亦不對第三方數據源負責。</strong></p>
</div>
