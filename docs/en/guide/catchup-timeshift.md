# Catchup & Timeshift (Usage and Customization)

This page explains how to use Catchup/Replay and Timeshift in the player, and how to customize time-parameter generation via the M3U `catchup-source` template (works for both HTTP unicast and RTSP unicast).

## Concepts and relationships

- **Catchup (Replay)**: pick a past program from the EPG list; the player generates a replay URL using the program start/end time and plays it.
- **Timeshift**: while watching live, drag the seek bar back to a past time; the player generates a replay URL using the timeshift cursor time and plays it.
- **Key point**: the player does not interpret ISP/private protocol semantics. It only replaces time placeholders inside a URL template, then plays the final URL.

### Playback Speed
- Speed selection is available for Timeshift and Replay; Live does not support speed
- Speed list: 0.5×, 0.75×, 1.0×, 1.25×, 1.5×, 1.75×, 2.0×, 3.0×, 5.0×
- Audio pitch correction is enabled automatically when changing speed

## Prerequisites

A channel can support Catchup/Timeshift if at least one is true:

- The channel provides a **`catchup-source`** in M3U (recommended; per-channel customization)
- You configured a **global Replay/Timeshift URL template** in Settings (fallback when `catchup-source` is missing)

Catchup relies on EPG program timing:

- With EPG data: accurate start/end times.
- Without EPG data: catchup may be unavailable or only work with placeholder programs (depends on your source).

## In-player actions (Catchup)

1. Play a channel (live).
2. Open the EPG panel (program list on the left).
3. Click a program marked as “Replay/Catchup” (typically programs before “now”).
4. The player generates a replay URL and starts playback; the status indicator becomes “Replay”.

## In-player actions (Timeshift)

1. While playing live, enable “Timeshift”.
2. Drag the seek bar back to a target time.
3. Release the mouse; the player generates a replay URL for that time and starts playback; the status becomes “Timeshift”.
4. Disable Timeshift to return to live.

## M3U format (recommended: channel-provided template)

Add `catchup-source` to the `#EXTINF` line:

```m3u
#EXTINF:-1 tvg-id="CCTV1" tvg-name="CCTV1" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",CCTV1
https://example.com/live/index.m3u8
```

Notes:

- `catchup-source` is the replay URL template. It can be the same as the live URL, or a dedicated replay endpoint.
- `catchup` values like `default/append/shift` are playlist ecosystem hints. For this player, the key is whether `catchup-source` exists and can produce a valid URL.
- A parameter-only `catchup-source` (for example `?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}`) is appended to the channel live URL (joined with `&` when the live URL already carries a query). Writing `catchup="append"` states the same intent explicitly.
- When the live URL comes from **rtp2httpd** (its default port `5140`, or a path containing `/rtsp/`) you need **no configuration at all**: the player appends the archive request itself as `playseek=START-END` (UTC while the "rtp2httpd time zone marker" is off, local time once it is on). A configured global template still takes priority over this fallback.

## Time placeholders (core of customization)

You can use these placeholders in `catchup-source` (or global templates):

### 1) Generic `${(b)FORMAT}` / `${(e)FORMAT}` (recommended)

- `${(b)FORMAT}`: begin/start time
- `${(e)FORMAT}`: end time
- `FORMAT` is a time format string
- Default output uses local time; if `FORMAT` ends with `|UTC`, it outputs UTC

Local time example:

```text
?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}
```

UTC example:

```text
?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}
```

### 2) `{utc:FORMAT}` / `{utcend:FORMAT}` (UTC)

```text
?begin={utc:yyyyMMddHHmmss}&end={utcend:yyyyMMddHHmmss}
```

### 3) `{start}` / `{end}` (fixed local format)

```text
?start={start}&end={end}
```

### 4) rtp2httpd Compatible Macros

To facilitate migration from rtp2httpd, SrcBox now natively supports the following shorthands (formats that rtp2httpd supports but SrcBox previously lacked):

- `YmdHMS` -> `yyyyMMddHHmmss` (14 digits)
- `Ymd` -> `yyyyMMdd`
- `HMS` -> `HHmmss`
- `${timestamp}` -> Unix Timestamp (10-digit seconds)
- `${duration}` -> Duration (seconds)

Mapping for other rtp2httpd formats:

- `yyyyMMddHHmmssGMT` -> `${(b)yyyyMMddHHmmss}GMT`
- ISO 8601 (UTC + Z) -> `${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z`
- ISO 8601 (Local + Offset) -> `${(b)yyyy-MM-ddTHH:mm:ssK}`

Example:

```text
?playseek={utc:YmdHMS}-{utcend:YmdHMS}
?start=${timestamp}&duration=${duration}
```

### 5) Unix timestamp (seconds) — begin/end

The player supports the following 10‑digit Unix timestamp placeholders:

- Begin: `${timestamp}`, `{timestamp}`, `${(b)timestamp}`, `${(b)unix}`, `${(b)epoch}`
- End: `${end_timestamp}`, `{end_timestamp}`, `${(e)timestamp}`, `${(e)unix}`, `${(e)epoch}`
- Duration (seconds): `${duration}`, `{duration}`

Common interface examples:

```text
// 1) start/end parameter interface
?start=${timestamp}&end=${end_timestamp}

// 2) playseek (begin-end)
playseek=${(b)timestamp}-${(e)timestamp}

// 3) begin + duration
?start=${timestamp}&duration=${duration}
```

M3U integration examples:

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&end=${end_timestamp}",Demo
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="Demo" catchup="append" catchup-source="https://example.com/live/index.m3u8?playseek=${(b)timestamp}-${(e)timestamp}",Demo
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&duration=${duration}",Demo
https://example.com/live/index.m3u8
```

## Common template examples

### HTTP unicast (HLS m3u8, UTC + `T`)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",Demo
https://example.com/live/index.m3u8
```

### RTSP unicast (PLTV-style `playseek`)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="append" catchup-source="rtsp://example.com/live.smil?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",Demo
rtsp://example.com/live.smil
```

### HTTP/RTSP generic (starttime/endtime)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/stream?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}",Demo
https://example.com/live/stream
```
## Priority and Override

- Channel `catchup-source` → builds the base URL and time placeholders (recommended per channel).
- Settings Replay/Timeshift template → used as a fallback when the channel doesn't provide `catchup-source`.
- Time Override (Settings → Time Override) → when enabled, rewrites only the time section (layout/keys/encoding), leaving domain/path and non-time params intact. Applies to both channel and fallback templates.
- Exception: a URL that already carries a `playseek`/`tvdr` archive request (rtp2httpd and similar) is only rewritten by the "playseek" layout; every other layout leaves it untouched, so the server never answers a replay request with the live stream.
- Tip: first generate a working URL via channel/fallback template, then use Time Override to unify the time expression required by your provider (e.g., starttime/endtime, UTC, or Unix seconds).

## Missing formats / private placeholders

- If your format isn't listed here, or your provider uses private placeholders/path-based time:
  - Try enabling Time Override and pick the closest layout/encoding;
  - Or file an issue (with examples). We will evaluate adding presets or more generic customization.
- Issues: https://github.com/CGG888/SrcBox/issues
```

## More placeholder examples (richer formats)

- **RFC3339/ISO-8601 with timezone**
  - UTC with `Z`:  
    `start=${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z&end=${(e)yyyy-MM-ddTHH:mm:ss|UTC}Z`
  - Auto output timezone or `Z` (depending on DateTime kind):  
    `start=${(b)yyyy-MM-ddTHH:mm:ssK}&end=${(e)yyyy-MM-ddTHH:mm:ssK}`
  - Explicit offset (e.g. `+08:00`):  
    `start=${(b)yyyy-MM-ddTHH:mm:ss}(${(b)zzz})&end=${(e)yyyy-MM-ddTHH:mm:ss}(${(e)zzz})`

- **Date-only or time-only**
  - `begin_date=${(b)yyyyMMdd}&begin_time=${(b)HHmmss}`
  - `end_date=${(e)yyyyMMdd}&end_time=${(e)HHmmss}`

- **Milliseconds (if supported by source)**
  - `start=${(b)yyyyMMddHHmmssfff}&end=${(e)yyyyMMddHHmmssfff}`

- **Equivalent curly UTC form**
  - `begin={utc:yyyy-MM-ddTHH:mm:ss}&end={utcend:yyyy-MM-ddTHH:mm:ss}`

Notes:
- `FORMAT` follows .NET DateTime format strings; `|UTC` means convert to UTC before formatting.
- `K` outputs `Z` for UTC and offset for local; `zzz` always outputs offset (e.g. `+08:00`).
- Whether milliseconds, `T`, and offsets are required depends on your source protocol.

## Relationship with Settings (recommended usage)

- Prefer **per-channel `catchup-source`** in M3U for stability.
- Use Settings templates as a **global fallback** when a channel does not provide `catchup-source`.

## Troubleshooting

- If logs still show `${(b)...}`/`{utc:...}` unchanged, the template was not applied or the channel is not using `catchup-source`.
- If the generated URL looks correct but playback fails, the source side may require different parameter names, time formats, or timezone (local vs UTC).
