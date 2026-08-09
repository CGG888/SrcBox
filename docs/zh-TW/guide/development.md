# 開發指南

> 完整的專案架構和技術細節請參閱 [完整架構文檔](/ARCHITECTURE.md)。

## 環境依賴

- **作業系統**：Windows 10 / 11 (x64)
- **開發工具**：Visual Studio 2022 或 JetBrains Rider
- **SDK**：.NET 8.0 SDK
- **依賴庫**：`libmpv-2.dll` (必須手動放置在輸出目錄)

## 編譯與執行

```powershell
# 還原依賴
dotnet restore

# 編譯（Debug）
dotnet build

# 執行
dotnet run
```

> **注意**：執行前請確保 `libmpv-2.dll` 已放置在 `bin\Debug\net8.0-windows\` 目錄下，否則程式會閃退或報錯。

## 故障排查

| 現象 | 可能原因 | 解決方案 |
| :--- | :--- | :--- |
| **程式啟動即崩潰** | 缺少 `libmpv-2.dll` | 下載對應架構的 dll 放入執行目錄 |
| **有畫面無聲音** | 音訊流探測超時 | 屬正常優化策略，可嘗試切換音軌或重啟播放 |
| **EPG 顯示「無數據」** | 網路問題或格式不支援 | 檢查 XMLTV URL 是否可訪問，是否為 GZIP 格式 |
| **設定不儲存** | 權限不足 | 確保程式目錄有寫入權限 |

## 測試與貢獻

### 貢獻流程

1. **Fork** 本倉庫。
2. 建立特性分支：`git checkout -b feature/AmazingFeature`。
3. 提交程式碼：`git commit -m 'feat: Add some AmazingFeature'` (請遵循 [Conventional Commits](https://www.conventionalcommits.org/))。
4. 推送分支：`git push origin feature/AmazingFeature`。
5. 提交 **Pull Request**。

### 程式碼規範

- 保持現有的 C# 程式碼風格（K&R / Allman 混合，視檔案而定，建議遵循 .editorconfig）。
- UI 修改請注意深色/淺色主題適配。

### 效能基準

- **CPU 佔用**：1080p 播放時應 < 15% (i5-8250U 基準)。
- **記憶體佔用**：穩定播放時應 < 500MB。
- **啟動時間**：冷啟動 < 2秒。
