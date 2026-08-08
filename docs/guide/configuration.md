# 配置说明

SrcBox 提供两类配置方式：**图形界面配置** 和 **JSON 文件配置**。

## 图形界面配置

通过设置窗口（`Ctrl+,`）可以配置大部分选项：

### 播放设置

| 选项 | 说明 | 默认值 |
|------|------|--------|
| **硬件解码** | 启用 D3D11VA 硬件加速 | 开启 |
| **FCC 快速切台** | 启用 FCC 预取优化 | 开启 |
| **FCC 预取数量** | 预取并行数量 | 2 |
| **UDP 组播优化** | 优化 UDP 组播播放 | 关闭 |
| **换源超时** | 超时时间（秒） | 3 |
| **时移缓冲** | 时移回看最大时长（小时） | 2 |

### 音频设置

| 选项 | 说明 | 范围 |
|------|------|------|
| **音量增益** | 音频增益 | -200dB ~ +60dB |
| **最大音量** | 音量上限 | 100% ~ 1000% |
| **音频延迟** | 音视频同步调整 | -100s ~ +100s |

### 录制设置

| 选项 | 说明 |
|------|------|
| **本地目录** | 录制保存路径，支持 `{channel}` 占位符 |
| **保存模式** | 本地优先 / 上传优先 |
| **WebDAV** | 配置服务器地址、用户名、密码 |

### 界面设置

| 选项 | 说明 |
|------|------|
| **主题模式** | 深色 / 浅色 / 跟随系统 |
| **语言** | 简体中文 / 繁体中文 / English / Русский |
| **关闭确认** | 退出时弹出确认框 |

---

## JSON 文件配置

`user_settings.json` 位于程序运行目录，存储高级用户配置。

### 完整配置示例

```json
{
  "Hwdec": true,
  "FccPrefetchCount": 2,
  "EnableUdpOptimization": false,
  "SourceTimeoutSec": 3,
  "TimeshiftHours": 2,
  "RecordingLocalDir": "recordings/{channel}",
  "Recording": {
    "Enabled": true,
    "SaveMode": "local_then_upload",
    "UploadMaxConcurrency": 1
  },
  "ScheduledReminders": [],
  "Language": "zh-CN",
  "ThemeMode": "System",
  "ConfirmOnClose": true
}
```

### 参数说明

| 参数 | 类型 | 说明 |
|------|------|------|
| `Hwdec` | bool | 开启硬件加速（默认 `d3d11va`） |
| `FccPrefetchCount` | int | FCC 预取数量，影响切台速度 |
| `EnableUdpOptimization` | bool | UDP 组播优化开关 |
| `SourceTimeoutSec` | int | 换源超时时间（秒） |
| `TimeshiftHours` | int | 时移最大时长（小时） |
| `RecordingLocalDir` | string | 录制目录模板 |
| `Recording.Enabled` | bool | 启用录制功能 |
| `Recording.SaveMode` | string | `local_only` / `local_then_upload` |
| `Recording.UploadMaxConcurrency` | int | 上传并发数 |
| `Language` | string | 语言代码 |
| `ThemeMode` | string | `Dark` / `Light` / `System` |
| `ConfirmOnClose` | bool | 关闭确认 |

---

## 配置优先级

```
图形界面设置 > JSON 文件设置 > 程序默认值
```

修改 JSON 文件后需要重启播放器生效。

---

## 配置文件位置

| 位置 | 说明 |
|------|------|
| `user_settings.json` | 用户设置（程序运行目录） |
| `app.log` | 运行日志（程序运行目录） |

---

## 常见问题

**Q: 修改 JSON 后需要重启吗？**
A: 是的，JSON 配置在启动时读取，修改后需要重启播放器。

**Q: 如何恢复默认设置？**
A: 删除 `user_settings.json`，播放器将使用默认配置重新创建。

**Q: 设置不保存？**
A: 确保程序目录有写入权限，或尝试以管理员身份运行。
