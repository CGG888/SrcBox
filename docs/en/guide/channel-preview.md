# Channel Preview

When you hover over a channel in the channel list, a live preview snapshot popup appears, allowing you to quickly preview content before switching channels.

![Channel Preview](/screenshots/videosnapshot.png)

## Prerequisites

The channel preview feature relies on **rtp2httpd**'s `X-Request-Snapshot` HTTP Header.

Make sure your rtp2httpd has snapshot support enabled:

```bash
rtp2httpd --enable-snapshot
```

## Features

- **Instant on hover**: Move mouse into a channel list item, snapshot popup immediately appears to the left
- **Non-intrusive**: Snapshot only shows on hover, automatically closes when double-clicking to play a channel
- **Customizable size**: Default 214×120 (16:9), adjustable in settings
- **LRU cache**: 30-second cache, no duplicate requests for the same channel within 30 seconds
- **Concurrency control**: Max 2 concurrent snapshot requests to prevent rtp2httpd overload

## Settings

In **Settings → Channel** page:

1. Check **Channel Preview** to enable the feature (disabled by default)
2. Optionally adjust **Width** and **Height** (min 80×60)
3. Recommended to keep 16:9 aspect ratio

## How It Works

The feature works by sending an HTTP request with `X-Request-Snapshot: 1` header to rtp2httpd to trigger snapshot generation, which returns the current video frame as JPEG. SrcBox ensures a smooth experience with:

- Asynchronous snapshot requests that don't block the UI thread
- In-memory caching, returning cached results for repeat requests within 30 seconds
- Maximum 2 concurrent requests to prevent rtp2httpd overload

## Use Cases

- **Channel browsing**: Quickly browse multiple channels without playing them
- **Channel switching**: Preview target channel content before switching
- **Multi-screen monitoring**: Hover to preview any channel's current画面 in multi-screen mode
