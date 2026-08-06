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

## 工作流编排 (Workflow Orchestration)

### 1. 计划节点模式 (Plan Node Default)
- 任何复杂任务（3+ 步骤）先进入计划模式
- 一旦发现偏离，立即停止并重新规划，避免硬做
- 验证后也要使用计划模式，不要直接干活
- **先写规格，再写代码**

### 2. 子代理策略 (Subagent Strategy)
- 优先使用子代理处理复杂的大面积改动
- 如需重构或新功能探索，完全委托给子代理
- 分配任务时通常通过子代理投喂上下文
- **每个子代理只专注一件事 (One task per subagent)**

### 3. 自我改进循环 (Self-Improvement Loop)
- 用户任何一次反馈，都将模式记录到 `tasks/lessons.md`
- 写反馈要简洁，防止重复犯错
- 积累 lessons，直到了然于胸
- **每次会话开始时首先复习项目的 lessons**

### 4. 完成前验证 (Verification Before Done)
- 每次验证意味着能够确保任务真正完成
- 需要时对比修复前后的行为差异
- **如果剩余任务是"工程师准备完毕"，就完成了**
- 进行测试、错误日志展示确认准确性

### 5. 要求优雅 (Demand Elegance)
- 做任何改动时停顿思考，用户是否有更优雅的方式
- 不要 hacky - 如果知道路线，一心中就实现优雅版本
- **考虑不必要的复杂性和技术债务**
- 每次提交前都要审视自己的代码

### 6. 自主 Bug 修复 (Autonomous Bug Fixing)
- 收到 bug 直接修复，不需要用户额外授权
- 指出日志对应失败测试，然后解释
- 用户决定如何处理失败的测试
- **自动修复失败的 CI 测试**

## 任务管理 (Task Management)

1. **先规划**：将计划写入 `tasks/todo.md`，使用可复选清单
2. **验证计划**：实现前先 check-in 确认
3. **减少介入**：每完成一项就打勾
4. **日报检查**：每条标注提供结果汇总
5. **记录改进**：在 `tasks/todo.md` 末尾添加 review 小节
6. **汲取教训**：总结的经验添加到 `tasks/lessons.md`

## 核心原则 (Core Principles)

- **保持简单**：每一步操作尽量简单，只影响最小范围
- **像 Senior Developer**：复杂业务逻辑，遇到错误及时修正，接近高级开发者的标准
- **最小影响**：只改必要改动，聚焦修复 bug

## 禁止事项

- **每次修改完代码，编译成功后，必须主动提交 git**
- **提交 git 每次都是到 main 分支**
- **commit 使用英文**

## 代码提交规范

### Commit 格式
```
<type>: <subject>

<body>
```

### Type 类型
| Type | 说明 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `docs` | 文档更新 |
| `style` | 代码格式（不影响功能）|
| `refactor` | 重构（不影响功能）|
| `perf` | 性能优化 |
| `test` | 测试相关 |
| `chore` | 构建/工具变更 |

### Commit 示例
```
feat: add scheduled recording with front/back modes

- Add ReminderDialog with record button
- Add ScheduledRecordingManager for managing recordings
- Add BackgroundRecordingInstance for back recording
- Add ScheduledRecordingListWindow UI
```

### PR 格式
```
## 描述
<简要说明本次变更>

## 变更内容
- <变更点1>
- <变更点2>

## 测试情况
- [ ] 已编译成功
- [ ] 已手动测试（如适用）
```

### PR 示例
```
## 描述
实现预约录制功能，支持前台录制和后台录制两种模式。

## 变更内容
- 新增 ScheduledRecordingManager 管理预约录制
- 新增 BackgroundRecordingInstance 后台录制实例
- 新增 RecordModeDialog 选择前台/后台录制
- 新增 ScheduledRecordingListWindow 管理窗口
- ReminderDialog 支持录制按钮
- ReminderService 支持 record_front/record_back 触发
- MenuBuilder 菜单调整

## 测试情况
- [x] 已编译成功
- [x] 已手动测试前台录制
- [x] 已手动测试后台录制
```

## 沟通与代码规范

- **沟通语言**: 使用中文，包括分析过程
- **代码注解**: 使用英文

## 国际化

- 每次修改完功能后，需要同步处理国际化
- 需编辑全部 4 个语言文件: `Strings.zh-CN.xaml`, `Strings.en-US.xaml`, `Strings.zh-TW.xaml`, `Strings.ru-RU.xaml`
