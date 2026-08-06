# SrcBox - Agent 指令

## 构建与测试

```powershell
dotnet build
dotnet test .\Tests\LibmpvIptvClient.Tests.csproj
```

## 架构

- **播放器核心**: `MpvPlayer.cs` + `MpvPlayerEngineAdapter.cs` 封装 libmpv-2.dll
- **主 ViewModel**: `Architecture/Presentation/Mvvm/MainWindow/MainShellViewModel.cs` - 中央状态管理
- **菜单构建**: `Helpers/MenuBuilder.cs` - 顶部菜单; `MainWindowMenuManager.cs` - 右键菜单
- **M3U/EPG 服务**: `Services/M3UCacheService.cs`, `Services/M3UParser.cs`, `Services/EpgService.cs`
- **分部类模式**: MainWindow 逻辑拆分在 `MainWindow.*.partial.cs` 文件中

## 关键约定

- 分部类模式: `MainWindow.xaml` + `MainWindow.Core.partial.cs`, `MainWindow.EventsAndStartup.partial.cs` 等
- MenuBuilder 回调模式: `refreshChannels: null` 表示未绑定; 通过 `MainWindowMenuManager.CreateAppMenu()` 绑定
- M3U 缓存 TTL 由 `AppSettings.Current.M3uCacheTtlHours` 控制，默认 12 小时
- 国际化: `Resources/Strings.{locale}.xaml` - 需编辑全部 4 个语言文件（中/英/繁/俄）

## 重要模式

- `M3UCacheService.Instance.RemoveCache(url)` 在强制刷新前清除缓存
- `MainShellViewModel.ForceRefreshChannels(url)` = 清除缓存 + 重新加载
- `LoadChannels(string url)` 遵循缓存 TTL; 不会强制下载
- 音量设置 (gain/max) 在 `MpvPlayer.Initialize()` 中应用 - 不会动态更改
- mpv 属性通过 `SetString("property-name", value)` / `SetDouble()` / `SetFlag()` 设置

## 常见任务

**添加菜单项**: 在 MenuBuilder.cs 中定义回调，在 MainWindowMenuManager.cs 中绑定

**添加快捷键**: 添加到 `MainWindowShortcutAction` 枚举 + `ResolveAction()` + `ExecuteAction()`

**添加设置**: 添加到 `PlaybackSettings.cs`，在 SettingsWindow 或对应的 Settings*ViewModel 中绑定

## 禁止事项

- **不说提交的时候，不得主动提交 git**
- **提交 git 每次都是到 main 分支**
- **commit 使用英文**

## 沟通与代码规范

- **沟通语言**: 使用中文，包括分析过程
- **代码注解**: 使用英文

## 国际化

- 每次修改完功能后，需要同步处理国际化
- 需编辑全部 4 个语言文件: `Strings.zh-CN.xaml`, `Strings.en-US.xaml`, `Strings.zh-TW.xaml`, `Strings.ru-RU.xaml`
