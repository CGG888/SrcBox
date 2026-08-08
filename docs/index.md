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
  - title: FCC 极速切台
    details: 毫秒级快速切台，告别传统 IPTV 的漫长等待
    icon: ⚡
  - title: M3U + EPG
    details: 支持本地/远程 M3U 播放列表，完整 XMLTV 电子节目单
    icon: 📺
  - title: 时移与回看
    details: 实时拖动回看直播历史，模板自动生成回放链接
    icon: ⏪
  - title: 预约录制
    details: 前台/后台双模式录制，支持 WebDAV 云端同步
    icon: 🎬
  - title: 多屏播放
    details: 支持 4/6/9 屏幕同时观看，数字键快速切换
    icon: 🖥️
  - title: Web 远程控制
    details: 浏览器直接操控播放器，随时随地掌控电视
    icon: 📱
  - title: 硬件解码
    details: D3D11VA/DXVA2/NVDEC/软件多模式自动切换
    icon: 🔧
  - title: 频道管理
    details: 分组、搜索、收藏、历史，灵活的列表管理
    icon: 📋
---

<ClientOnly>
  <VideoShowcase locale="zh" />
</ClientOnly>

<ClientOnly>
  <ScreenshotGallery locale="zh" />
</ClientOnly>

<ClientOnly>
  <TechStack locale="zh" />
</ClientOnly>

<ClientOnly>
  <HelpSection locale="zh" />
</ClientOnly>

<div class="disclaimer">
  <strong>免责声明：</strong> 本页面展示的所有视频、截图及演示画面仅作功能展示用途，并非实际可播放或可用的媒体资源。<strong>本项目不提供任何 m3u 播放列表文件及其中包含的频道数据，亦不对第三方数据源负责。</strong>
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
