# 回看与时移（使用与自定义）

本页说明播放器内如何使用回看/时移，以及如何通过 M3U 的 `catchup-source` 自定义时间参数生成规则（HTTP 单播与 RTSP 单播通用）。

## 概念与关系

- **回看（Catchup/Replay）**：从 EPG 节目单中选择已播出的节目，按节目起止时间生成回放地址并播放。
- **时移（Timeshift）**：在“直播”的基础上，拖动进度条回退到过去某个时间点；播放器会按“时移游标时间”生成回放地址并播放。
- **关键点**：播放器不理解运营商私有协议的语义，只负责把 URL 模板中的“时间占位符”替换成具体时间字符串，然后播放替换后的 URL。

### 倍速播放
- 倍速选择仅在“时移/回放”可用，直播不支持
- 支持速度：0.5×、0.75×、1.0×、1.25×、1.5×、1.75×、2.0×、3.0×、5.0×
- 切换倍速时自动开启音高校正，确保人声自然

## 前置条件

要让某频道支持回看/时移，至少满足其一：

- **频道在 M3U 中提供了 `catchup-source`**（推荐；每个频道可单独定制）
- **播放器设置中配置了全局回放/时移地址模板**（当频道没有 `catchup-source` 时作为兜底）

同时，回看依赖节目单时间：

- 有 EPG 数据时：回看能使用准确的节目开始/结束时间。
- 没有 EPG 数据时：回看入口可能不可用或只能按占位节目生成时间段（效果取决于你的源是否容忍）。

## 播放器内操作（回看）

1. 选择并播放一个频道（进入直播）。
2. 打开 EPG 面板（左侧节目单）。
3. 在节目单中，点击标记为“回放”的节目（通常是当前时间之前的节目）。
4. 播放器会生成“带时间参数”的回放 URL 并开始播放，状态标识变为“回放”。

## 播放器内操作（时移）

1. 在直播播放中，开启“时移”（界面上会显示时移状态）。
2. 拖动进度条回退到想看的时间点。
3. 松开后播放器会按该时间点生成回放 URL 并播放，状态标识为“时移”。
4. 关闭时移后会返回直播。

## M3U 写法（推荐：频道自带回看模板）

在 `#EXTINF` 行增加 `catchup-source`，示例：

```m3u
#EXTINF:-1 tvg-id="CCTV1" tvg-name="CCTV1" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",CCTV1-综合
https://example.com/live/index.m3u8
```

说明：

- `catchup-source` 是“回放地址模板”，可以与直播地址相同，也可以是另一条专用回放入口。
- `catchup` 常见有 `default/append/shift`，用于给播放列表/播放器生态标注“回看模式”；本播放器的关键是 `catchup-source` 是否存在且可生成有效 URL。
- `catchup-source` 只写参数段（例如 `?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}`）时，会自动拼接到该频道的直播地址后（直播地址已带 `?` 时改用 `&`）；写 `catchup="append"` 可显式声明这种拼接。
- 直播地址是 **rtp2httpd** 的（默认端口 `5140`，或路径里带 `/rtsp/`）时**可以完全不配置**：播放器会自动按 `playseek=开始-结束` 追加回看参数（未开启「rtp2httpd 时区标记」时用 UTC，开启后用本地时间）；已配置的全局模板仍然优先于这个自动回退。

## 时间占位符（自定义的核心）

你可以在 `catchup-source`（或全局模板）中使用这些占位符：

### 1）通用 `${(b)FORMAT}` / `${(e)FORMAT}`（推荐）

- `${(b)FORMAT}`：开始时间
- `${(e)FORMAT}`：结束时间
- `FORMAT` 是时间格式字符串
- 默认按本地时间输出；若 `FORMAT` 以 `|UTC` 结尾，则按 UTC 输出

示例（本地时间）：

```text
?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}
```

示例（UTC 时间）：

```text
?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}
```

### 2）`{utc:FORMAT}` / `{utcend:FORMAT}`（UTC）

- `{utc:FORMAT}`：开始时间（UTC）
- `{utcend:FORMAT}`：结束时间（UTC）

示例：

```text
?begin={utc:yyyyMMddHHmmss}&end={utcend:yyyyMMddHHmmss}
```

### 3）`{start}` / `{end}`（本地时间固定格式）

- `{start}`：开始时间（本地，固定 `yyyyMMddHHmmss`）
- `{end}`：结束时间（本地，固定 `yyyyMMddHHmmss`）

示例：

```text
?start={start}&end={end}
```

### 4）rtp2httpd 兼容简写宏

为了方便从 rtp2httpd 迁移，SrcBox 原生支持了以下简写（rtp2httpd 支持但 SrcBox 原本没有的格式）：

- `YmdHMS` -> `yyyyMMddHHmmss`（14 位时间）
- `Ymd` -> `yyyyMMdd`
- `HMS` -> `HHmmss`
- `${timestamp}` -> Unix 时间戳（秒，10 位）
- `${duration}` -> 持续时长（秒）

rtp2httpd 其他格式的对应写法：

- `yyyyMMddHHmmssGMT` -> `${(b)yyyyMMddHHmmss}GMT`
- ISO 8601 (UTC + Z) -> `${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z`
- ISO 8601 (Local + Offset) -> `${(b)yyyy-MM-ddTHH:mm:ssK}`

示例：

```text
?playseek={utc:YmdHMS}-{utcend:YmdHMS}
?start=${timestamp}&duration=${duration}
```

### 5）Unix 时间戳（秒）扩展（开始/结束）

支持以下“秒级 Unix 时间戳”占位符（10 位）：

- 开始时间：`${timestamp}`、`{timestamp}`、`${(b)timestamp}`、`${(b)unix}`、`${(b)epoch}`
- 结束时间：`${end_timestamp}`、`{end_timestamp}`、`${(e)timestamp}`、`${(e)unix}`、`${(e)epoch}`
- 时长（秒）：`${duration}`、`{duration}`

常见接口示例：

```text
// 1) start/end 参数接口
?start=${timestamp}&end=${end_timestamp}

// 2) playseek 接口（开始-结束）
playseek=${(b)timestamp}-${(e)timestamp}

// 3) 开始+时长
?start=${timestamp}&duration=${duration}
```

M3U 集成示例：

```m3u
#EXTINF:-1 tvg-name="示例频道" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&end=${end_timestamp}",示例频道
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="示例频道" catchup="append" catchup-source="https://example.com/live/index.m3u8?playseek=${(b)timestamp}-${(e)timestamp}",示例频道
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="示例频道" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&duration=${duration}",示例频道
https://example.com/live/index.m3u8
```

## 常用模板示例

### HTTP 单播（HLS m3u8，UTC + T）

```m3u
#EXTINF:-1 tvg-name="示例频道" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",示例频道
https://example.com/live/index.m3u8
```

### RTSP 单播（PLTV 常见 playseek）

```m3u
#EXTINF:-1 tvg-name="示例频道" catchup="append" catchup-source="rtsp://example.com/live.smil?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",示例频道
rtsp://example.com/live.smil
```

### RTSP/HTTP 通用（starttime/endtime）

```m3u
#EXTINF:-1 tvg-name="示例频道" catchup="default" catchup-source="https://example.com/live/stream?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}",示例频道
https://example.com/live/stream

## 优先级与覆盖

- 频道 `catchup-source` → 生成基础地址与时间占位（推荐每频道独立配置）。
- 设置中的“回放/时移模板” → 当频道没有 `catchup-source` 时作为兜底生成。
- 时间覆盖（设置页“时间覆盖”）→ 若启用，只重写“时间片段”（布局/键名/编码），不改域名/路径/非时间参数；对频道模板与兜底模板均生效。
- 调试建议：先用频道模板或兜底模板生成可播放链接，再用“时间覆盖”统一为运营商要求的时间表达（如 starttime/endtime、UTC 或 Unix 秒）。

## 反馈私有时间占位符

- 如需本文未覆盖的时间格式，或运营商使用私有占位/路径式时间片段：
  - 可在设置页启用“时间覆盖”选择最接近的布局与编码进行适配；
  - 或到 Issues 提交需求（附示例与说明），我们会评估加入预设或提供更通用的自定义能力。
- 提交地址：https://github.com/CGG888/SrcBox/issues
```

## 更多写法示例（丰富格式）

- **RFC3339/ISO-8601（带时区信息）**
  - 以 UTC 输出并带 Z：  
    `start=${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z&end=${(e)yyyy-MM-ddTHH:mm:ss|UTC}Z`
  - 自动输出本地偏移或 Z（依时间类型）：  
    `start=${(b)yyyy-MM-ddTHH:mm:ssK}&end=${(e)yyyy-MM-ddTHH:mm:ssK}`
  - 指定偏移（例如 +08:00）：  
    `start=${(b)yyyy-MM-ddTHH:mm:ss}(${(b)zzz})&end=${(e)yyyy-MM-ddTHH:mm:ss}(${(e)zzz})`

- **仅日期或仅时间**
  - `begin_date=${(b)yyyyMMdd}&begin_time=${(b)HHmmss}`
  - `end_date=${(e)yyyyMMdd}&end_time=${(e)HHmmss}`

- **毫秒/微秒片段（取决于源是否支持）**
  - `start=${(b)yyyyMMddHHmmssfff}&end=${(e)yyyyMMddHHmmssfff}`

- **花括号 UTC 写法的等价形式**
  - `begin={utc:yyyy-MM-ddTHH:mm:ss}&end={utcend:yyyy-MM-ddTHH:mm:ss}`

提示：
- `FORMAT` 使用 .NET 时间格式，`|UTC` 代表以 UTC 转换再格式化。
- `K` 会在 UTC 时输出 `Z`，在本地时输出偏移；`zzz`始终输出偏移（如 `+08:00`）。
- 是否支持毫秒、是否需要 `T`、是否要求带偏移，取决于源端协议，请按实际要求选择。

## 与“设置”的关系（建议用法）

- **优先使用 M3U 的 `catchup-source`**：每个频道独立，最稳定。
- **设置里的模板**：更适合做全局兜底（当某些频道不提供 `catchup-source` 时统一生成）。

## 排错建议

- 点击回看后，日志里仍出现 `${(b)...}`/`{utc:...}` 这类文本：说明模板占位符没有被替换或没有走到回看流程，优先检查该频道是否真的在使用 `catchup-source`。
- 回看 URL 生成正确但无法播放：通常是源端不支持该参数名/时间格式/时区，尝试改用本地时间或 UTC，或更换为源端要求的参数名（例如 `playseek` 与 `starttime/endtime` 的差异）。
