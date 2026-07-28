---
layout: home

hero:
  name: "源匣 (SrcBox)"
  text: "高性能 Windows IPTV 播放器"
  tagline: "基于 libmpv 内核，支持 EPG、时移回看、Web 远程控制、极速切台的现代化 IPTV 播放工具。"
  image:
    src: /logo.svg
    alt: 源匣 Logo
  actions:
    - theme: brand
      text: 开始使用
      link: /guide/
    - theme: alt
      text: 访问 GitHub
      link: https://github.com/CGG888/SrcBox

features:
  - title: 现代化界面
    details: 基于 WPF/ModernWpf 构建，完美适配 Windows 10/11 深色与浅色主题，提供流畅的原生体验。
    icon: 🎨
  - title: 极致性能
    details: 采用 libmpv 播放内核，支持硬件解码 (d3d11va)，低资源占用，毫秒级快速切台。
    icon: 🚀
  - title: 极速切台 (FCC)
    details: 针对 IPTV 场景深度优化的快速切台技术，告别缓冲等待，享受丝滑换台体验。
    icon: ⚡
  - title: 智能节目单
    details: 完整支持 XMLTV (gz) 格式 EPG，支持自动按日切换和智能节目匹配。
    icon: 📅
  - title: 时移与回看
    details: 支持直播流实时拖动时移，以及基于模板自动生成的 Catchup 回放，不错过任何精彩瞬间。
    icon: ⏪
  - title: 预约与提醒
    details: 支持节目预约通知与到点自动播放，提供预约列表和批量管理能力。
    icon: ⏰
  - title: 录播与上传
    details: 支持本地录播、录播索引与 WebDAV 上传队列，适配本地/远端双模式。
    icon: ⬆️
  - title: Web 远程控制
    details: 通过浏览器远程控制播放器，支持播放控制、音量调节、频道切换和状态查看。
    icon: 📱
  - title: 音频设置
    details: 支持音量增益、最大音量限制和音频延迟调节，打造最佳听感体验。
    icon: 🔊
  - title: 去交错处理
    details: 针对 1080i/720i 隔行扫描视频流优化，支持自动检测和多种去交错算法。
    icon: 🖼️
  - title: 频道管理
    details: 提供频道分组、搜索、收藏与历史记录，本地持久化管理播放列表。
    icon: 📺
  - title: 多语言支持
    details: 支持简体中文、繁体中文、英文、俄文等多种语言界面。
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

## 项目简介

**源匣 (SrcBox)** 是一款专为 Windows 平台打造的高性能、现代化的 IPTV 播放器。

它基于强大的 **libmpv** 播放内核构建，结合 **WPF** 的现代化界面设计，为您带来流畅、稳定的直播观看体验。不仅支持 M3U 播放列表、EPG 电子节目单、回看、时移、录播等核心功能，还针对 IPTV 场景进行了深度优化（如 FCC 快速切台、UDP 组播优化）。

### 核心特性

- **libmpv 播放内核**：高性能、低资源占用，支持硬件加速解码
- **FCC 快速切台**：针对 IPTV 场景优化的毫秒级极速切台
- **EPG 节目单**：完整支持 XMLTV 格式，智能节目匹配
- **时移回看**：支持直播时移和基于模板的 Catchup 回放
- **Web 远程控制**：通过浏览器远程操控播放器
- **音频优化**：音量增益、最大音量限制、音频延迟调节
- **去交错处理**：针对隔行扫描视频流的智能处理
- **WebDAV 录播**：本地录播配合云端同步
- **多语言界面**：支持中文（简/繁）、英文、俄文

## 功能演示

<div style="display: flex; flex-direction: column; gap: 60px; align-items: center; padding-bottom: 40px;">
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>毫秒级切台 (FCC)</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">极致优化的快速换台体验</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Catchup / 节目回放</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">基于模板自动生成回看地址，不错过精彩节目</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/catchup.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Timeshift / 直播时移</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">实时拖动进度条，随时回看直播历史</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/timeshift.mp4" type="video/mp4">
        Your browser does not support the video tag.
      </video>
    </ClientOnly>
  </div>
</div>

## 界面预览

<div style="display: flex; gap: 20px; overflow-x: auto; padding-bottom: 20px; justify-content: start;">
  <img src="/screenshots/main.png" alt="主界面" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/fullscreen-overlay.png" alt="全屏悬浮" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/settings.png" alt="设置" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
</div>

## 技术栈

| 组件 | 技术 |
|------|------|
| 播放内核 | libmpv |
| UI 框架 | WPF + ModernWpf |
| 语言 | C# (.NET 8) |
| 录播同步 | WebDAV |
| 远程控制 | WebSocket + HTTP |

## 获取帮助

- 📖 [使用指南](/guide/) - 详细的功能介绍和配置说明
- 🐛 [问题反馈](https://github.com/CGG888/SrcBox/issues) - 报告 Bug 或提出建议
- 📦 [下载安装](https://github.com/CGG888/SrcBox/releases) - 获取最新版本

<div style="margin-top: 40px; padding-top: 20px; border-top: 1px solid var(--vp-c-divider); font-size: 14px; color: var(--vp-c-text-2);">
  <p><strong>免责声明：</strong> 本页面展示的所有视频、截图及演示画面仅作功能展示用途，并非实际可播放或可用的媒体资源。<strong>本项目不提供任何 m3u 播放列表文件及其中包含的频道数据，亦不对第三方数据源负责。</strong></p>
</div>
