# SrcBox 项目架构文档

> 本文档详细说明 SrcBox 项目的代码结构和架构设计。

## 目录

- [项目概述](#项目概述)
- [目录结构](#目录结构)
- [核心架构](#核心架构)
- [模块说明](#模块说明)
- [设计模式](#设计模式)
- [依赖关系](#依赖关系)

---

## 项目概述

**SrcBox** 是一款专为 Windows 平台打造的高性能、现代化 IPTV 播放器。

| 属性 | 说明 |
|------|------|
| **框架** | WPF (.NET 8.0) |
| **播放内核** | libmpv-2.dll |
| **UI 库** | ModernWpf |
| **架构** | MVVM + 分层架构 |
| **许可** | MIT |

---

## 目录结构

```
SrcBox/
├── 📂 根目录                       # WPF 必需文件
├── 📂 Architecture/                # 分层架构核心
├── 📂 Controls/                    # UI 组件/抽屉
├── 📂 Converters/                  # 值转换器
├── 📂 Diagnostics/                 # 诊断工具
├── 📂 Helpers/                     # 辅助工具类
├── 📂 Models/                      # 数据模型
├── 📂 Playback/                    # 播放控制
├── 📂 Resources/                   # 国际化资源
├── 📂 Services/                    # 核心服务层
├── 📂 Styles/                      # 样式资源
└── 📂 Windows/                     # 窗口/对话框
```

---

## 根目录 (9 文件)

| 文件 | 说明 | 可移动？ |
|------|------|:---:|
| `App.xaml` / `App.xaml.cs` | WPF 程序入口 | ❌ |
| `MainWindow.xaml` | 主窗口 XAML | ❌ |
| `MainWindow.*.partial.cs` (5个) | 主窗口分片逻辑 | ❌ |
| `MpvPlayer.cs` | libmpv 封装核心 | ⚠️ |

**说明**：这些文件是 WPF 框架硬性要求，必须位于根目录。

---

## Architecture/ 分层架构

采用 **CQRS + 分层架构**，分为以下层级：

```
Architecture/
├── Application/          # 应用层 - 业务逻辑和配置
├── Platform/            # 平台层 - 底层能力抽象
├── Presentation/        # 表现层 - UI 和交互
├── Core/                # 核心 - 基础设施
├── Plugin/              # 插件 - 扩展机制
└── Tooling/             # 工具 - 构建和诊断
```

### Application/ 应用层

业务逻辑和配置管理。

```
Application/
├── Settings/                          # 设置模块
│   ├── PlaybackSettings.cs            # 播放设置模型
│   ├── IWebDavSettingsService.cs      # WebDAV 设置接口
│   ├── WebDavSettingsService.cs       # WebDAV 设置实现
│   ├── SettingsLegalDocumentService.cs
│   └── UpdateService.cs               # 更新服务
├── Player/
│   └── IPlayerEngine.cs               # 播放器引擎接口
├── Shared/
│   ├── SharedUiContracts.cs           # UI 契约
│   └── SharedUiServices.cs            # UI 服务
└── OverlayVisibilityPolicy.cs         # 全屏可见性策略
```

### Platform/ 平台层

底层能力和第三方库封装。

```
Platform/
├── Player/
│   └── MpvPlayerEngineAdapter.cs      # libmpv 适配器
├── PlatformAdapterFactory.cs          # 适配器工厂
├── PlatformAdapters.cs                # 平台适配器
└── PlatformContracts.cs               # 平台契约
```

### Presentation/ 表现层

UI 展示和交互逻辑，采用 MVVM 模式。

```
Presentation/
├── Mvvm/                              # MVVM 基础
│   ├── ViewModelBase.cs               # ViewModel 基类
│   ├── ObservableObject.cs            # 可观察对象
│   ├── RelayCommand.cs                # 命令中继
│   ├── AsyncCommand.cs                # 异步命令
│   │
│   ├── MainWindow/                    # 主窗口 ViewModel (24个)
│   │   ├── MainShellViewModel.cs      # 主 Shell
│   │   ├── MainWindowChannelPlaybackActionsViewModel.cs
│   │   ├── MainWindowChannelListActionsViewModel.cs
│   │   ├── MainWindowEpgActionsViewModel.cs
│   │   ├── MainWindowHistoryActionsViewModel.cs
│   │   ├── MainWindowMenuActionsViewModel.cs
│   │   ├── MainWindowRecordingActionsViewModel.cs
│   │   ├── MainWindowShortcutActionsViewModel.cs
│   │   ├── MainWindowSourceLoaderViewModel.cs
│   │   └── ... (更多)
│   │
│   └── Settings/                      # 设置 ViewModel (20个)
│       ├── SettingsWindowUiActionsViewModel.cs
│       ├── SettingsPlaybackViewModel.cs
│       ├── SettingsSaveCoordinatorViewModel.cs
│       └── ... (更多)
│
└── View/                              # 视图管理器 (11个)
    ├── MainWindowMenuManager.cs
    ├── MainWindowOverlayManager.cs
    ├── MainWindowSettingsManager.cs
    ├── MainWindowRecordingManager.cs
    ├── MainWindowEpgManager.cs
    └── ... (更多)
```

### Core/ 核心

基础设施和依赖注入。

```
Core/
├── SrcBoxKernel.cs                    # 内核 (DI 容器)
├── SrcBoxArchitectureHost.cs          # 架构宿主
├── ServiceRegistry.cs                 # 服务注册表
├── RuntimePlatform.cs                 # 运行时平台
├── VersionPolicy.cs                   # 版本策略
└── PluginContracts.cs                 # 插件契约
```

### Plugin/ 插件

插件机制支持。

```
Plugin/
├── PluginManager.cs                   # 插件管理器
├── PluginLoadContext.cs               # 插件加载上下文
├── PluginManifest.cs                  # 插件清单
└── PluginRuntimeContracts.cs          # 运行时契约
```

### Tooling/ 工具

构建和诊断工具。

```
Tooling/
├── BuildPipelinePlanner.cs            # 构建规划
├── PerformanceProfiler.cs             # 性能分析
├── PluginTemplateGenerator.cs         # 插件模板生成
└── ResourceReclaimer.cs               # 资源回收
```

---

## Controls/ UI 组件

抽屉控件和自定义组件。

| 文件 | 说明 |
|------|------|
| **Components/** | 公共组件 |
| `ModernMessageBox.xaml` | 现代消息框 |
| `OverlayControls.xaml` | 悬浮控制栏 |
| `TopOverlay.xaml` | 顶部悬浮层 |
| **Drawers/** | 抽屉面板 |
| `EpgDrawer.xaml` | EPG 抽屉 |
| `PlaybackDrawer.xaml` | 播放设置抽屉 |
| `LogoDrawer.xaml` | Logo 设置抽屉 |
| `RecordingDrawer.xaml` | 录制设置抽屉 |
| `TimeshiftDrawer.xaml` | 时移设置抽屉 |
| `UploadQueueDrawer.xaml` | 上传队列抽屉 |
| `VolumeSlider.xaml` | 音量滑块 |
| **Windows/** | 子窗口 |
| `MultiScreenWindow.xaml` | 多屏播放窗口 |
| `DeleteRecordingDialog.xaml` | 删除录制确认 |

---

## Services/ 服务层

核心业务服务。

| 服务 | 说明 |
|------|------|
| **播放相关** | |
| `M3UParser.cs` | M3U 播放列表解析 |
| `TxtParser.cs` | TXT 格式解析 |
| `ChannelService.cs` | 频道管理服务 |
| `MediaProbeService.cs` | 媒体探测服务 |
| `UrlTimeRewriter.cs` | URL 时间重写 (回看/时移) |
| **EPG 相关** | |
| `EpgService.cs` | EPG 节目单服务 |
| `EpgMatcher.cs` | EPG 匹配器 |
| **录制相关** | |
| `HttpTsRecorder.cs` | TS 录制器 |
| `RecordingIndexService.cs` | 录制索引服务 |
| `ScheduledRecordingManager.cs` | 预约录制管理 |
| `BackgroundRecordingInstance.cs` | 后台录制实例 |
| **网络相关** | |
| `HttpClientService.cs` | HTTP 客户端 |
| `HttpClientExtensions.cs` | HTTP 扩展 |
| `ConnectionPreheater.cs` | 连接预热 |
| `DnsPrefetcher.cs` | DNS 预取 |
| `IptvCheckerClient.cs` | IPTV 检查客户端 |
| **存储相关** | |
| `M3UCacheService.cs` | M3U 缓存服务 |
| `LogoCacheService.cs` | Logo 缓存服务 |
| `UserDataStore.cs` | 用户数据存储 |
| `WebDavClient.cs` | WebDAV 客户端 |
| `UploadQueueService.cs` | 上传队列服务 |
| **其他** | |
| `AudioManager.cs` | 音频管理 |
| `ReminderService.cs` | 预约提醒服务 |
| `ToastService.cs` | Toast 通知服务 |
| `NotificationService.cs` | 通知服务 |
| `CryptoUtil.cs` | 加密工具 |
| `RegistryProxyProvider.cs` | 注册表代理 |
| `TsDurationEstimator.cs` | TS 时长估算 |
| **Web 远程** | |
| `WebRemote/WebRemoteManager.cs` | 远程控制管理器 |
| `WebRemote/WebRemoteServer.cs` | 远程控制服务器 |
| `WebRemote/WebRemoteStrings.cs` | 远程控制字符串 |

---

## Models/ 数据模型

| 模型 | 说明 |
|------|------|
| `Channel.cs` | 频道模型 |
| `Source.cs` | 播放源模型 |
| `EPGProgram.cs` | EPG 节目模型 |
| `ChannelPool.cs` | 频道池模型 |
| `ScheduledRecordingInfo.cs` | 预约录制信息 |

---

## Windows/ 窗口和对话框

```
Windows/
├── Dialogs/              # 对话框 (6 对)
│   ├── AutoPlayModeDialog.xaml     # 自动播放模式
│   ├── RecordModeDialog.xaml       # 录制模式选择
│   ├── ReminderDialog.xaml         # 预约提醒
│   ├── ReminderToastWindow.xaml    # 预约 Toast
│   ├── TextViewerDialog.xaml       # 文本查看
│   └── UpdateDialog.xaml           # 更新对话框
│
├── Lists/                # 列表窗口 (3 对)
│   ├── M3uListWindow.xaml          # M3U 列表管理
│   ├── ReminderListWindow.xaml     # 预约列表
│   └── ScheduledRecordingListWindow.xaml  # 预约录制列表
│
├── Others/               # 其他窗口 (6 对)
│   ├── AboutWindow.xaml            # 关于窗口
│   ├── AddM3uWindow.xaml           # 添加 M3U
│   ├── DebugWindow.xaml            # 调试窗口
│   ├── EditM3uWindow.xaml          # 编辑 M3U
│   ├── FullscreenWindow.xaml       # 全屏窗口
│   └── UploadQueueWindow.xaml      # 上传队列
│
└── Settings/             # 设置窗口 (2 对)
    ├── SettingsWindow.xaml         # 设置主窗口
    └── ShortcutsWindow.xaml        # 快捷键说明
```

---

## 设计模式

### 1. MVVM 模式

ViewModel 通过继承 `ViewModelBase` 实现属性变更通知，通过 `RelayCommand` / `AsyncCommand` 处理命令。

```csharp
// ViewModel 示例
public class MainShellViewModel : ViewModelBase
{
    private string _title;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
}
```

### 2. 分层架构

```
┌─────────────────────────────────────┐
│     Presentation (表现层)            │
│   ViewModels + Views + Controls     │
├─────────────────────────────────────┤
│     Application (应用层)             │
│   Settings + Services               │
├─────────────────────────────────────┤
│     Platform (平台层)                │
│   Player Adapters                   │
├─────────────────────────────────────┤
│     Core (核心层)                    │
│   DI Container + Infrastructure     │
└─────────────────────────────────────┘
```

### 3. 依赖注入

通过 `SrcBoxKernel` 实现依赖注入，服务注册在 `ServiceRegistry` 中。

```csharp
// 服务注册
public void RegisterServices()
{
    Kernel.Bind<IChannelService>().To<ChannelService>();
    Kernel.Bind<IEpgService>().To<EpgService>();
}
```

### 4. Partial Class 模式

`MainWindow` 按功能域拆分为多个 partial class 文件：

| 文件 | 职责 |
|------|------|
| `MainWindow.xaml` | XAML 定义 |
| `MainWindow.Core.partial.cs` | 核心逻辑 |
| `MainWindow.EventsAndStartup.partial.cs` | 事件和启动 |
| `MainWindow.Fullscreen.partial.cs` | 全屏逻辑 |
| `MainWindow.LifecycleAndEpg.partial.cs` | 生命周期和 EPG |
| `MainWindow.PlaybackRecordings.partial.cs` | 播放和录制 |

### 5. 命令模式

通过 `RelayCommand` 和 `AsyncCommand` 实现命令绑定。

```csharp
public ICommand PlayCommand { get; }
public AsyncCommand LoadChannelsCommand { get; }
```

---

## 依赖关系

### 核心依赖

```
App.xaml (入口)
    │
    ├── MainWindow.xaml (主窗口)
    │       │
    │       ├── MpvPlayer.cs (播放器核心)
    │       │
    │       ├── Architecture/Presentation/View/ (视图管理器)
    │       │       │
    │       │       └── Architecture/Presentation/Mvvm/ (ViewModels)
    │       │
    │       ├── Architecture/Application/ (应用层)
    │       │       │
    │       │       ├── Architecture/Platform/Player/ (平台适配器)
    │       │       │       │
    │       │       │       └── MpvPlayer.cs (实际调用 libmpv)
    │       │       │
    │       │       └── Architecture/Application/Settings/ (配置)
    │       │
    │       ├── Services/ (服务层)
    │       │
    │       └── Models/ (数据模型)
    │
    ├── Controls/ (UI 组件)
    │
    └── Resources/ (资源文件)
```

### 服务依赖

```
ChannelService
    ├── M3UParser
    ├── TxtParser
    ├── M3UCacheService
    └── IptvCheckerClient

EpgService
    └── EpgMatcher

RecordingManager
    ├── HttpTsRecorder
    ├── WebDavClient
    └── RecordingIndexService

WebRemoteManager
    └── HttpClientService
```

---

## 命名规范

| 类型 | 规范 | 示例 |
|------|------|------|
| 类名 | PascalCase | `MainShellViewModel` |
| 方法名 | PascalCase | `LoadChannelsAsync` |
| 属性名 | PascalCase | `ChannelName` |
| 私有字段 | _camelCase | `_channelList` |
| 接口名 | I + PascalCase | `IPlayerEngine` |
| XAML 资源 | PascalCase | `MenuStyles.xaml` |

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.1 - 1.1.9 | 2024-2025 | 初始版本迭代 |
| 1.1.10 | 2025-08 | 当前版本 |

---

## 贡献指南

1. 遵循现有代码风格 (参考 `.editorconfig`)
2. UI 修改需适配深色/浅色主题
3. 新增服务需在 `ServiceRegistry` 中注册
4. 使用 `Diagnostic.Logger` 进行日志记录
5. 提交遵循 [Conventional Commits](https://www.conventionalcommits.org/)
