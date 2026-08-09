# 技术架构

本项目采用 **C# / WPF** 开发，核心架构如下：

- **UI 层**：基于 WPF (ModernWpf)，提供流畅的现代化交互体验。
- **架构层**：`Architecture/` 下按 Application / Platform / Presentation 分层，播放、设置、同步逻辑均模块化拆分。
- **互操作层**：通过 `MpvPlayer.cs` 与 `MpvPlayerEngineAdapter` 封装 libmpv 调用。
- **渲染层**：利用 `WindowsFormsHost` 承载 Win32 窗口句柄，将 mpv 的渲染输出嵌入 WPF 界面，解决 WPF 原生媒体元素性能不足的问题。
- **服务层**：
  - `M3UParser`：高效的正则表达式解析器，支持极其复杂的 M3U 扩展标签。
  - `EpgService`：基于 `XmlSerializer` 的异步 EPG 加载与内存缓存机制。
  - `RecordingIndexService / UploadQueueService / WebDavClient`：录播索引、上传队列与远端存储链路。
  - `ReminderService`：预约通知与预约播放调度。

## 项目结构

详细项目结构请参阅 [完整架构文档](https://github.com/CGG888/SrcBox/blob/main/ARCHITECTURE.md)。

```text
📂 SrcBox
├── 📂 Architecture    # 分层架构 (Application/Platform/Presentation)
├── 📂 Services        # 核心服务 (M3U/EPG/录播/WebDAV/通知等)
├── 📂 Controls        # 抽屉与弹窗控件 (EPG/录播/时移/上传队列)
├── 📂 Resources       # 国际化与主题资源
├── 📂 Tests           # MSTest 自动化测试
├── 📄 MainWindow.*.cs # 主窗口分片逻辑
└── 📄 MpvPlayer.cs    # libmpv 封装
```

## 关键业务模块

- **预约模块**：`ReminderService` + `ScheduledReminder`，支持”仅提醒/自动播放”策略。
- **录播模块**：`MainWindowRecordingManager` 负责开始/停止录制、元数据写入、列表刷新。
- **上传模块**：`UploadQueueService` + `WebDavClient` 负责上传排队、失败重试、远端目录组织。
- **精简模式**：由主窗口状态与 Overlay 协调，保证窗口态/全屏态标识与交互同步。

## libmpv 引擎说明

本项目依赖 `libmpv-2.dll`。

- **硬解**：默认开启 `d3d11va`。
- **无声问题**：部分 IPTV 源音频探测较慢，已设置 `probesize=32` 加速起播，可能导致短暂无声。
