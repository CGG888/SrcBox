# 回看與時移（使用與自訂）

本頁說明播放器內如何使用回看/時移，以及如何透過 M3U 的 `catchup-source` 自訂時間參數生成規則（HTTP 單播與 RTSP 單播通用）。

## 概念與關係

- **回看（Catchup/Replay）**：從 EPG 節目單中選擇已播出的節目，按節目起止時間生成回放位址並播放。
- **時移（Timeshift）**：在「直播」的基礎上，拖動進度條回退到過去某個時間點；播放器會按「時移游標時間」生成回放位址並播放。
- **關鍵點**：播放器不理解營運商私有協議的語意，只負責把 URL 模板中的「時間佔位符」替換成具體時間字串，然後播放替換後的 URL。

### 倍速播放
- 倍速選擇僅在「時移/回放」可用，直播不支援
- 支援速度：0.5×、0.75×、1.0×、1.25×、1.5×、1.75×、2.0×、3.0×、5.0×
- 切換倍速時自動開啟音高校正，確保人聲自然

## 前置條件

要讓某頻道支援回看/時移，至少滿足其一：

- **頻道在 M3U 中提供了 `catchup-source`**（推薦；每個頻道可單獨定制）
- **播放器設定中配置了全域回放/時移位址模板**（當頻道沒有 `catchup-source` 時作為兜底）

同時，回看依賴節目單時間：

- 有 EPG 資料時：回看能使用準確的節目開始/結束時間。
- 沒有 EPG 資料時：回看入口可能不可用或只能按占位節目生成時間段（效果取決於你的源是否容忍）。

## 播放器內操作（回看）

1. 選擇並播放一個頻道（進入直播）。
2. 打開 EPG 面板（左側節目單）。
3. 在節目單中，點擊標記為「回放」的節目（通常是當前時間之前的節目）。
4. 播放器會生成「帶時間參數」的回放 URL 並開始播放，狀態標識變為「回放」。

## 播放器內操作（時移）

1. 在直播播放中，開啟「時移」（界面上會顯示時移狀態）。
2. 拖動進度條回退到想看的時間點。
3. 放開後播放器會按該時間點生成回放 URL 並播放，狀態標識為「時移」。
4. 關閉時移後會返回直播。

## M3U 寫法（推薦：頻道自帶回放模板）

在 `#EXTINF` 行增加 `catchup-source`，示例：

```m3u
#EXTINF:-1 tvg-id="CCTV1" tvg-name="CCTV1" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",CCTV1-綜合
https://example.com/live/index.m3u8
```

說明：

- `catchup-source` 是「回放位址模板」，可以與直播位址相同，也可以是另一條專用回放入口。
- `catchup` 常見有 `default/append/shift`，用於給播放清單/播放器生態標註「回看模式」；本播放器的關鍵是 `catchup-source` 是否存在且可生成有效 URL。
- `catchup-source` 只寫參數段（例如 `?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}`）時，會自動拼接到該頻道的直播位址後（直播位址已帶 `?` 時改用 `&`）；寫 `catchup="append"` 可明確宣告這種拼接。
- 直播位址是 **rtp2httpd** 的（預設連接埠 `5140`，或路徑裡帶 `/rtsp/`）時**可以完全不設定**：播放器會自動以 `playseek=開始-結束` 追加回看參數（未開啟「rtp2httpd 時區標記」時用 UTC，開啟後用本地時間）；已設定的全域模板仍然優先於這個自動回退。

## 時間佔位符（自訂的核心）

你可以在 `catchup-source`（或全域模板）中使用這些佔位符：

### 1）通用 `${(b)FORMAT}` / `${(e)FORMAT}`（推薦）

- `${(b)FORMAT}`：開始時間
- `${(e)FORMAT}`：結束時間
- `FORMAT` 是時間格式字串
- 預設按本地時間輸出；若 `FORMAT` 以 `|UTC` 結尾，則按 UTC 輸出

示例（本地時間）：

```text
?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}
```

示例（UTC 時間）：

```text
?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}
```

### 2）`{utc:FORMAT}` / `{utcend:FORMAT}`（UTC）

- `{utc:FORMAT}`：開始時間（UTC）
- `{utcend:FORMAT}`：結束時間（UTC）

示例：

```text
?begin={utc:yyyyMMddHHmmss}&end={utcend:yyyyMMddHHmmss}
```

### 3）`{start}` / `{end}`（本地時間固定格式）

- `{start}`：開始時間（本地，固定 `yyyyMMddHHmmss`）
- `{end}`：結束時間（本地，固定 `yyyyMMddHHmmss`）

示例：

```text
?start={start}&end={end}
```

### 4）rtp2httpd 相容簡寫巨集

為了方便從 rtp2httpd 遷移，SrcBox 原生支援了以下簡寫（rtp2httpd 支援但 SrcBox 原本沒有的格式）：

- `YmdHMS` -> `yyyyMMddHHmmss`（14 位時間）
- `Ymd` -> `yyyyMMdd`
- `HMS` -> `HHmmss`
- `${timestamp}` -> Unix 時間戳（秒，10 位）
- `${duration}` -> 持續時長（秒）

rtp2httpd 其他格式的對應寫法：

- `yyyyMMddHHmmssGMT` -> `${(b)yyyyMMddHHmmss}GMT`
- ISO 8601 (UTC + Z) -> `${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z`
- ISO 8601 (Local + Offset) -> `${(b)yyyy-MM-ddTHH:mm:ssK}`

示例：

```text
?playseek={utc:YmdHMS}-{utcend:YmdHMS}
?start=${timestamp}&duration=${duration}
```

### 5）Unix 時間戳（秒）擴展（開始/結束）

支援以下「秒級 Unix 時間戳」佔位符（10 位）：

- 開始時間：`${timestamp}`、`{timestamp}`、`${(b)timestamp}`、`${(b)unix}`、`${(b)epoch}`
- 結束時間：`${end_timestamp}`、`{end_timestamp}`、`${(e)timestamp}`、`${(e)unix}`、`${(e)epoch}`
- 時長（秒）：`${duration}`、`{duration}`

常見介面示例：

```text
// 1) start/end 參數介面
?start=${timestamp}&end=${end_timestamp}

// 2) playseek 介面（開始-結束）
playseek=${(b)timestamp}-${(e)timestamp}

// 3) 開始+時長
?start=${timestamp}&duration=${duration}
```

M3U 整合示例：

```m3u
#EXTINF:-1 tvg-name="示例頻道" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&end=${end_timestamp}",示例頻道
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="示例頻道" catchup="append" catchup-source="https://example.com/live/index.m3u8?playseek=${(b)timestamp}-${(e)timestamp}",示例頻道
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="示例頻道" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&duration=${duration}",示例頻道
https://example.com/live/index.m3u8
```

### 6）Kodi / APTV / TV Live 通用詞彙

SrcBox 也認得 Kodi（pvr.iptvsimple）文件裡的那套佔位符，APTV / TV Live / IPTV-org 清單裡的 `catchup-source` 可以直接抄過來。issue #6 裡重慶聯通的案例就是按 Kodi 文件寫 `?time={utc}`，當時 `{utc}` 原樣留在了請求裡，伺服器當然不認。

- 開始時間（UTC Unix 秒，10 位）：`{utc}`、`${start}`
- 結束時間（UTC Unix 秒，10 位）：`{utcend}`、`${end}`
- 當前時間（UTC Unix 秒，10 位）：`{lutc}`、`${now}`
- 開始時間的年月日時分秒（本地時間）：`{Y}`、`{m}`、`{d}`、`{H}`、`{M}`、`{S}`
- 時長除以 X：`{duration:X}`（如 `{duration:60}` 得到分鐘）
- 當前時間與開始時間之差除以 X：`{offset:X}`（如 `{offset:60}` 得到分鐘）

說明：

- `{utc:FORMAT}` / `{utcend:FORMAT}` / `${start:FORMAT}` / `${end:FORMAT}` 都支援。寫成 Kodi 方言（只用 `Y m d H M S`，如 `{utc:YmdHM}`）按 Kodi 含義翻譯；寫成 .NET 格式（如 `${utc:yyyyMMddHHmmss}`）原樣使用。
- 兩套方言裡 `m` / `M` 的含義相反（Kodi 的 `m` 是月、`M` 是分，.NET 反之），混寫時建議直接用 `yyyyMMdd…` 的 .NET 寫法。
- `{timestamp}` 在 SrcBox 裡表示**開始時間**（歷史行為，本文檔一直這麼寫）；Kodi 的 `{timestamp}` 表示當前時間，需要當前時間請寫 `{lutc}` 或 `${now}`。
- `{catchup-id}`（XMLTV 節目級 ID）暫不支援。

常見寫法：

```text
?time={utc}
?utc={utc}&utcend={utcend}
?t={Y}-{m}-{d}T{H}:{M}:{S}
?start={utc:YmdHM}&end=${end:Ymd-H-M}
?offset={offset:60}&duration={duration:60}
```

## 常用模板示例

### HTTP 單播（HLS m3u8，UTC + T）

```m3u
#EXTINF:-1 tvg-name="示例頻道" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",示例頻道
https://example.com/live/index.m3u8
```

### RTSP 單播（PLTV 常見 playseek）

```m3u
#EXTINF:-1 tvg-name="示例頻道" catchup="append" catchup-source="rtsp://example.com/live.smil?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",示例頻道
rtsp://example.com/live.smil
```

### RTSP/HTTP 通用（starttime/endtime）

```m3u
#EXTINF:-1 tvg-name="示例頻道" catchup="default" catchup-source="https://example.com/live/stream?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}",示例頻道
https://example.com/live/stream
```

## 常用格式預設（設定頁）

設定 → 回放設定 / 時移設定 裡新增了「常用格式」下拉選單：選中即把模板填入「回看位址格式」/「時移位址格式」，仍可手動修改。預設全部是「僅參數段」，會自動拼到該頻道的直播位址後；頻道自帶 `catchup-source` 時（每頻道獨立設定）優先級更高，預設只作為全域兜底。

- **playseek 開始-結束（本地時間，中興/PLTV 常用）**：`?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}`
- **playseek 開始-結束（UTC 時間）**：`?playseek=${utc:yyyyMMddHHmmss}-${utcend:yyyyMMddHHmmss}`
- **playseek 開始-結束（Unix 秒）**：`?playseek=${timestamp}-${end_timestamp}`
- **單一 Unix 時間戳（Kodi/APTV 常用）**：`?time={utc}`
- **starttime/endtime（UTC + T，華為/魔百和常用）**：`?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}`
- **starttime/endtime（本地時間）**：`?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}`
- **start/end（Unix 秒）**：`?start=${timestamp}&end=${end_timestamp}`
- **start + duration（Unix 秒）**：`?start=${timestamp}&duration=${duration}`

逐個試到能回看為止即可，不需要改動 m3u。

## RTSP 單播回看（中興 / PLTV 等）

RTSP 單播（例如 `rtsp://IP:8089/04000001/<32 位 ID>`）能不能回看取決於伺服器端，播放器只負責把時間參數拼到位址上。

- 清單裡已經有 `catchup-source` 時（廣西聯通這類）：

  ```m3u
  #EXTINF:-1 tvg-name="CCTV1" catchup="append" catchup-source="?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",CCTV-1
  rtsp://103.251.96.219/PLTV/88888888/224/3221225656/xxxx.smil?zoneoffset=480&accountinfo=XXXXX&GuardEncType=2
  ```

  直播位址已帶 `?` 時參數用 `&` 拼接，認證參數原樣保留。

- 清單裡沒有任何回看標籤時（重慶聯通這類）：到設定頁用上面的「常用格式」逐個試，建議順序是 `playseek 開始-結束（本地時間）` → `單一 Unix 時間戳` → `starttime/endtime（UTC + T）`。
- 回看/時移時拖動進度條會**按新時間點重新請求回看位址**（RTSP 回看串流無法在播放器內部 seek）；頻道沒有 EPG 資料時，進度條以本次請求的回看視窗為範圍，因此同樣可以拖動。

## 優先級與覆蓋

- 頻道 `catchup-source` → 生成基礎位址與時間佔位（推薦每頻道獨立配置）。
- 設定中的「回放/時移模板」 → 當頻道沒有 `catchup-source` 時作為兜底生成。
- 時間覆蓋（設定頁「時間覆蓋」）→ 若啟用，只重寫「時間片段」（佈局/鍵名/編碼），不改網域/路徑/非時間參數；對頻道模板與兜底模板均生效。
- 例外：網址已帶 `playseek`/`tvdr` 的回看請求（rtp2httpd 等）只會被「playseek」佈局重寫；其它佈局一律保持原樣，避免伺服器把回看請求當成直播請求。
- 調試建議：先用頻道模板或兜底模板生成可播放連結，再用「時間覆蓋」統一為營運商要求的時間表達（如 starttime/endtime、UTC 或 Unix 秒）。

## 私有時間佔位符回饋

- 如需本文未覆蓋的時間格式，或營運商使用私有佔位/路徑式時間片段：
  - 可在設定頁啟用「時間覆蓋」選擇最接近的佈局與編碼進行適配；
  - 或到 Issues 提交需求（附示例與說明），我們會評估加入預設或提供更通用的自訂能力。
- 提交地址：https://github.com/CGG888/SrcBox/issues

## 更多寫法示例（豐富格式）

- **RFC3339/ISO-8601（帶時區資訊）**
  - 以 UTC 輸出並帶 Z：  
    `start=${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z&end=${(e)yyyy-MM-ddTHH:mm:ss|UTC}Z`
  - 自動輸出本地偏移或 Z（依時間類型）：  
    `start=${(b)yyyy-MM-ddTHH:mm:ssK}&end=${(e)yyyy-MM-ddTHH:mm:ssK}`
  - 指定偏移（例如 +08:00）：  
    `start=${(b)yyyy-MM-ddTHH:mm:ss}(${(b)zzz})&end=${(e)yyyy-MM-ddTHH:mm:ss}(${(e)zzz})`

- **僅日期或僅時間**
  - `begin_date=${(b)yyyyMMdd}&begin_time=${(b)HHmmss}`
  - `end_date=${(e)yyyyMMdd}&end_time=${(e)HHmmss}`

- **毫秒/微秒片段（取決於源是否支援）**
  - `start=${(b)yyyyMMddHHmmssfff}&end=${(e)yyyyMMddHHmmssfff}`

- **花括號 UTC 寫法的等價形式**
  - `begin={utc:yyyy-MM-ddTHH:mm:ss}&end={utcend:yyyy-MM-ddTHH:mm:ss}`

提示：
- `FORMAT` 使用 .NET 時間格式，`|UTC` 代表以 UTC 轉換再格式化。
- `K` 在 UTC 時輸出 `Z`，在本地時輸出偏移；`zzz`始終輸出偏移（如 `+08:00`）。
- 是否支援毫秒、是否需要 `T`、是否要求帶偏移，取決於源端協議，請按實際要求選擇。

## 與「設定」的關係（建議用法）

- **優先使用 M3U 的 `catchup-source`**：每頻道獨立，最穩定。
- **設定裡的模板**：更適合做全域兜底（當某些頻道不提供 `catchup-source` 時統一生成）。

## 排錯建議

- 點擊回看後，日誌裡仍出現 `${(b)...}`/`{utc:...}` 這類文本：說明模板佔位符沒有被替換或沒有走到回看流程，優先檢查該頻道是否真的在使用 `catchup-source`。
- 回看 URL 生成正確但無法播放：通常是源端不支援該參數名/時間格式/時區，嘗試改用本地時間或 UTC，或更換為源端要求的參數名（例如 `playseek` 與 `starttime/endtime` 的差異）。
