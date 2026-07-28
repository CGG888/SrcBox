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

## 優先級與覆蓋

- 頻道 `catchup-source` → 生成基礎位址與時間佔位（推薦每頻道獨立配置）。
- 設定中的「回放/時移模板」 → 當頻道沒有 `catchup-source` 時作為兜底生成。
- 時間覆蓋（設定頁「時間覆蓋」）→ 若啟用，只重寫「時間片段」（佈局/鍵名/編碼），不改網域/路徑/非時間參數；對頻道模板與兜底模板均生效。
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
