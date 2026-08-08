# 路標圖 (Roadmap)

我們致力於持續提升 IPTV 觀看體驗。

## <span style="font-size:1.2em">✨</span> 已完成

### 核心播放

<span style="color:#22c55e">✔</span> **FCC 極速切台** - 毫秒級頻道切換，針對 IPTV 深度優化<br>
<span style="color:#22c55e">✔</span> **M3U 播放列表** - 本地/遠程支援，UTF-8/GB18030 編碼，`#EXTINF` 擴展屬性<br>
<span style="color:#22c55e">✔</span> **M3U 二進制緩存** - ETag/Last-Modified 驗證，毫秒級載入<br>
<span style="color:#22c55e">✔</span> **EPG 電子節目單** - XMLTV (gz) 解析，央視/教育台類型後綴支援<br>
<span style="color:#22c55e">✔</span> **頻道回看 (Catchup)** - 模板自動生成回放 URL<br>
<span style="color:#22c55e">✔</span> **時移 (Time-Shift)** - 即時拖動回看，節目邊界內快進快退<br>
<span style="color:#22c55e">✔</span> **頻道管理** - 分組、搜尋、收藏、歷史，支援分組排序

### 播放優化

<span style="color:#22c55e">✔</span> **硬體解碼** - D3D11VA/DXVA2/NVDEC/軟體自動切換<br>
<span style="color:#22c55e">✔</span> **去交錯處理** - 1080i/720i 優化，yadif/bwdif 演算法<br>
<span style="color:#22c55e">✔</span> **音頻設定** - 音量增益、最大音量、音頻延遲<br>
<span style="color:#22c55e">✔</span> **倍速播放** - 時移/回放模式 0.5×~5.0×，音高校正<br>
<span style="color:#22c55e">✔</span> **自動換源** - 源失效時自動切換下一源

### 多屏與錄製

<span style="color:#22c55e">✔</span> **多屏播放** - 4/6/9 屏幕同時觀看，數字鍵快速選擇<br>
<span style="color:#22c55e">✔</span> **本地錄製** - 直接錄製到本地磁碟<br>
<span style="color:#22c55e">✔</span> **WebDAV 上傳** - 錄製後自動上傳到雲端<br>
<span style="color:#22c55e">✔</span> **預約錄製** - 前台/後台雙模式，定時自動停止

### 介面與互動

<span style="color:#22c55e">✔</span> **深色/淺色主題** - 完美適配 Windows 10/11<br>
<span style="color:#22c55e">✔</span> **全屏懸浮控制** - 滑鼠觸底顯示播放控制條<br>
<span style="color:#22c55e">✔</span> **側邊抽屜** - 頻道列表(右)和 EPG(左)<br>
<span style="color:#22c55e">✔</span> **精簡模式** - 緊湊視窗形態<br>
<span style="color:#22c55e">✔</span> **系統匣** - 常駐圖示，快速操作功能表<br>
<span style="color:#22c55e">✔</span> **快捷鍵支援** - 完整的鍵盤快捷鍵，快捷鍵幫助窗口<br>
<span style="color:#22c55e">✔</span> **關閉模式記憶** - 記住退出/最小化到系統匣的選擇

### 遠程與同步

<span style="color:#22c55e">✔</span> **Web 遠程控制** - 瀏覽器遠程操控播放器<br>
<span style="color:#22c55e">✔</span> **節目預約提醒** - 節目到點通知，支援自動播放策略<br>
<span style="color:#22c55e">✔</span> **多語言** - 簡體中文、繁體中文、English、Русский

## <span style="font-size:1.2em">🚧</span> 進行中

<span style="color:#f59e0b">⚙</span> **EPG 狀態晶片可點擊** - 點擊直接回到直播，交互評估中<br>
<span style="color:#f59e0b">⚙</span> **預約通知動畫** - 淡入/滑入動效方案已設計

## <span style="font-size:1.2em">📌</span> 未來計劃

<span style="color:#6b7280">○</span> **雲端錄製 (PVR)** - 連接遠端儲存進行節目錄製<br>
<span style="color:#6b7280">○</span> **播放鏈路優化** - 繼續降低切台延遲，優化弱網場景穩定性<br>
<span style="color:#6b7280">○</span> **多源治理** - 源健康檢測、自動降級與可觀測性日誌<br>
<span style="color:#6b7280">○</span> **測試體系擴展** - 補齊播放狀態機、錄播索引、EPG 同步相關單元測試<br>
<span style="color:#6b7280">○</span> **錄播體驗增強** - 錄製中信息同步、遠端元資料一致性
