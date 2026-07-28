# 技術架構

本專案採用 **C# / WPF** 開發，核心架構如下：

- **UI 層**：基於 WPF (ModernWpf)，提供流暢的現代化互動體驗。
- **架構層**：`Architecture/` 下按 Application / Platform / Presentation 分層，播放、設定、同步邏輯均模組化拆分。
- **互操作層**：通過 `MpvPlayer.cs` 與 `MpvPlayerEngineAdapter` 封裝 libmpv 呼叫。
- **渲染層**：利用 `WindowsFormsHost` 承載 Win32 視窗控制代碼，將 mpv 的渲染輸出嵌入 WPF 介面，解決 WPF 原生媒體元素效能不足的問題。
- **服務層**：
  - `M3UParser`：高效的正則表達式解析器，支援極其複雜的 M3U 擴展標籤。
  - `EpgService`：基於 `XmlSerializer` 的非同步 EPG 載入與記憶體快取機制。
  - `RecordingIndexService / UploadQueueService / WebDavClient`：錄播索引、上傳佇列與遠端儲存鏈路。
  - `ReminderService`：預約通知與預約播放調度。

## 專案結構

```text
📂 SrcBox
├── 📂 Architecture    # 分層架構 (Application/Platform/Presentation)
├── 📂 Services        # 核心服務 (M3U/EPG/錄播/WebDAV/通知等)
├── 📂 Controls        # 抽屜與彈窗控制項 (EPG/錄播/時移/上傳佇列)
├── 📂 Resources       # 國際化與主題資源
├── 📂 Tests           # MSTest 自動化測試
├── 📄 MainWindow.*.cs # 主視窗分片邏輯
└── 📄 MpvPlayer.cs    # libmpv 封裝
```

## 關鍵業務模組

- **預約模組**：`ReminderService` + `ScheduledReminder`，支援「僅提醒/自動播放」策略。
- **錄播模組**：`MainWindowRecordingManager` 負責開始/停止錄製、元資料寫入、列表重新整理。
- **上傳模組**：`UploadQueueService` + `WebDavClient` 負責上傳排隊、失敗重試、遠端目錄組織。
- **精簡模式**：由主視窗狀態與 Overlay 協調，保證視窗態/全螢幕態識別與互動同步。

## libmpv 引擎說明

本專案依賴 `libmpv-2.dll`。

- **硬解**：預設開啟 `d3d11va`。
- **無聲問題**：部分 IPTV 來源音訊探測較慢，已設定 `probesize=32` 加速起播，可能導致短暫無聲。
