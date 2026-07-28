# 配置說明

## `user_settings.json`

位於程式執行目錄，存儲使用者偏好設定。

```json
{
  "Hwdec": true,              // 硬體解碼開關
  "FccPrefetchCount": 2,      // FCC 預取數量
  "EnableUdpOptimization": false,
  "SourceTimeoutSec": 3,      // 換源超時時間（秒）
  "TimeshiftHours": 2,        // 時移回看時長（小時）
  "RecordingLocalDir": "recordings/{channel}",
  "Recording": {
    "Enabled": true,
    "SaveMode": "local_then_upload",
    "UploadMaxConcurrency": 1
  },
  "ScheduledReminders": [],
  "Language": "zh-CN",        // 介面語言
  "ThemeMode": "System",      // 主題模式 (Dark/Light/System)
  "ConfirmOnClose": true
}
```

## 參數說明

- **Hwdec**: 開啟硬體加速（預設使用 `d3d11va`）。
- **FccPrefetchCount**: FCC 預取並行數量，影響切台速度與資源佔用平衡。
- **EnableUdpOptimization**: UDP 組播優化開關。
- **SourceTimeoutSec**: 換源超時時間（秒），若來源無效則嘗試下一個。
- **TimeshiftHours**: 時移回看的最大時長（小時）。
- **RecordingLocalDir**: 錄播預設目錄模板（支援 `{channel}`）。
- **Recording.SaveMode**: 錄製保存模式（本地/上傳優先策略）。
- **ScheduledReminders**: 預約列表持久化資料，含「僅提醒/自動播放」策略。
- **Language**: 介面語言代碼（如 `zh-CN`, `en-US`）。
- **ThemeMode**: 應用程式主題模式（深色/淺色/跟隨系統）。
- **ConfirmOnClose**: 關閉視窗時是否彈出確認框（可最小化到系統匣）。

## 預約與錄播建議

- 預約播放場景建議保持 `ConfirmOnClose=true`，避免誤關導致錯過提醒。
- 錄播上傳場景建議按網路能力調整 `Recording.UploadMaxConcurrency`。
- 使用遠端儲存時建議同時配置 WebDAV（見設定頁「錄播」分組）。
