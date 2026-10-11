# 源匣（SrcBox）（Windows / WPF）

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)](https://www.microsoft.com/windows)
[![Version](https://img.shields.io/github/v/release/CGG888/SrcBox?display_name=tag&color=58a6ff)](https://github.com/CGG888/SrcBox/releases/latest)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](http://makeapullrequest.com)

[English](./README_EN.md) | [中文](./README.md)

**源匣（SrcBox）** 是一款专为 Windows 平台打造的高性能、现代化 IPTV 播放器。基于 **libmpv** 播放内核，结合 **WPF** 现代化界面，支持 M3U 播放列表、EPG 电子节目单、时移回看、预约录制、多屏播放、Web 远程控制等丰富功能。

[⬇️ 下载最新版本](https://github.com/CGG888/SrcBox/releases) | [📖 官方文档](https://srcbox.top) | [🐛 问题反馈](https://github.com/CGG888/SrcBox/issues)

---

> **免责声明**：
> 本页面展示的所有视频、截图及演示画面仅作**功能展示用途**，并非实际可播放或可用的媒体资源。**本项目不提供任何 m3u 播放列表文件及其中包含的频道数据**，亦不对第三方数据源负责。请遵守当地法律法规，使用合法播放源。

## 功能特性

| 功能 | 说明 |
|:---:|------|
| ⚡ **FCC 极速切台** | 毫秒级快速切台，告别传统 IPTV 的漫长等待 |
| 📸 **频道快照预览** | 鼠标悬停频道显示实时预览，需要 rtp2httpd 支持 |
| 📺 **M3U + EPG** | 支持本地/远程 M3U 播放列表，完整 XMLTV 电子节目单 |
| ⏪ **时移与回看** | 实时拖动回看直播历史，模板自动生成回放链接 |
| 🎬 **预约录制** | 前台/后台双模式录制，WebDAV 云端同步 |
| 🖥️ **多屏播放** | 支持 4/6/9 屏幕同时观看，数字键快速切换 |
| 📱 **Web 远程控制** | 手机扫码即用：标准遥控盘与完整界面，换台、回看、预约、录制管理 |
| 🔒 **HTTPS 与 PWA** | 单端口同时提供 http / https，内置自签证书，可安装到手机主屏幕 |
| 🔧 **硬件解码** | D3D11VA/DXVA2/NVDEC/软件多模式自动切换 |
| 📋 **频道管理** | 分组、搜索、收藏、历史，灵活的列表管理 |

## 系统需求 & 快捷键

| 系统需求 | |
|:---:|------|
| **操作系统** | Windows 10 / 11 (x64) |
| **运行环境** | .NET 8.0 SDK |
| **依赖库** | libmpv-2.dll（仓库根目录提供） |

| 常用快捷键 | |
|:---:|------|
| `Space` | 播放/暂停 |
| `Enter` | 全屏切换 |
| `↑↓` | 切换频道 |
| `E` | EPG 节目单 |
| `L` | 频道列表 |
| `R` | 开始/停止录制 |

## Web 远程控制（手机遥控）

启用后手机扫码即可打开遥控器页面，无需安装 App：

- **两种界面**：标准遥控（方向键、数字键、音量键、换源/静音、全屏）与完整界面（状态、播放控制、频道列表、EPG、源管理、预约与录制管理），选择自动记忆
- **单端口 HTTP + HTTPS**：同一个端口同时提供 `http://` 与 `https://`，内置自签证书可在设置页或遥控器页面下载（Android 装 CA 证书，iPhone 装描述文件并开启完全信任）
- **安装到主屏幕（PWA）**：Android Chrome / Edge 可直接「安装应用」，iOS Safari 通过「添加到主屏幕」安装，独立窗口全屏运行
- **换台联动**：从手机换台时，电视端会显示与遥控器输入一致的频道号

详细步骤见 [Web 远程控制文档](https://srcbox.top/guide/web-remote)（[English](https://srcbox.top/en/guide/web-remote)）。

## 最近更新 (v1.1.15)

- 🖥️ **全屏重构** - 主窗口自身进入全屏，浮层归属主窗口；退出后窗口几何与标题栏按钮全部还原
- 📱 **手机遥控免装 App** - 单端口 HTTP + HTTPS、自签证书下载、可安装到手机主屏幕（PWA）
- 🎨 **全屏配色与尺寸统一** - 浅色主题全屏也使用深色顶栏/底栏/浮窗；浮窗占可用高度 72% 并上下居中
- ⚡ **EPG 渐进加载** - 节目单按源逐步显示，不再等全部源解析完成
- 🧠 **记忆能力** - 按 M3U 源记住最后频道、按频道记住上次使用的源（可在设置中关闭）
- 🔊 **音量归一** - 默认音量 1.0，最大音量设置可把输出提升到 1.0 以上

[v1.1.15 更新说明 →](https://srcbox.top/releases/v1.1.15) ｜ [查看全部更新 →](https://github.com/CGG888/SrcBox/releases)

## 技术架构

| 层级 | 说明 |
|------|------|
| **UI 层** | WPF (ModernWpf) 现代化交互体验 |
| **架构层** | `Architecture/` 分层 (Application/Platform/Presentation) |
| **互操作层** | `MpvPlayer.cs` + `MpvPlayerEngineAdapter` 封装 libmpv |
| **服务层** | `M3UParser`、`EpgService`、`WebDAV` 等核心服务 |

## 快速开始

```powershell
# 编译
dotnet build

# 运行
dotnet run

# 测试
dotnet test .\Tests\LibmpvIptvClient.Tests.csproj
```

## 官方文档

| 语言 | 链接 |
|------|------|
| 简体中文 | [srcbox.top](https://srcbox.top) |
| 繁体中文 | [srcbox.top/zh-TW](https://srcbox.top/zh-TW) |
| English | [srcbox.top/en](https://srcbox.top/en) |
| Русский | [srcbox.top/ru](https://srcbox.top/ru) |

## 隐私与安全

- 所有配置、播放列表、EPG 缓存均存储于用户本地设备
- 网络请求仅在请求 M3U/EPG 地址、检查更新时发起

## 截图预览

- 主界面  
  ![main](docs/screenshots/main.png)

- 多屏播放  
  ![multi-screen](docs/screenshots/multiscreen.png)

- 设置窗口  
  ![settings](docs/screenshots/settings.png)

- Web 远程控制（标准遥控 · 深色 / 完整界面 · 深色）  
  ![remote-pad-dark](docs/screenshots/web-remote-pad-dark.png)
  ![remote-full-dark](docs/screenshots/web-remote-full-dark.png)

---

## 开源许可

本项目基于 [MIT License](./LICENSE.txt) 开源，您可以自由使用、修改和分发，但需保留原始版权声明。

## 致谢

- [libmpv](https://mpv.io/) - 强大的跨平台媒体播放引擎
- [ModernWpf](https://github.com/AngelSoyoso/ModernWpf) - WPF 现代化 UI 库
- [VitePress](https://vitepress.dev/) - 静态文档站点生成器
- [Downloader](https://github.com/avgp/ngx-downloader) - 分段下载库
