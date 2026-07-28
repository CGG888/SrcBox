# Features

## Core Playback
- **Playback Control**: Play/Pause, Stop, Seek, Fast Forward/Rewind.
- **Volume Control**: Slider adjustment, mute support (0-100).
- **Volume Gain**: Adjustable from -200dB to +60dB.
- **Volume Max**: Configurable maximum volume from 100% to 1000%.
- **Audio Delay**: Adjustable from -100s to +100s for audio sync.
- **Status Indicators**: Live/Replay/Timeshift status displayed in overlay.

## IPTV Specifics

### M3U Parsing
Supports local/remote M3U playlists, compatible with UTF-8/GB18030 encoding. Supports `#EXTINF` extended attributes.

### EPG Support
XMLTV (gz) format support with automatic day switching and `tvg-id` matching.
[Usage and configuration guide](./epg)

### Catchup (Replay)
Template-based catchup URL generation (e.g., `{utc:yyyyMMddHHmmss}`).
[Usage and customization guide](./catchup-timeshift)

<video controls width="100%" poster="/logo.svg">
  <source src="/screenshots/catchup.mp4" type="video/mp4">
  Your browser does not support the video tag.
</video>

### Timeshift
Seek back in live stream history (depends on `catchup-source`).
[Usage and customization guide](./catchup-timeshift)

<video controls width="100%" poster="/logo.svg">
  <source src="/screenshots/timeshift.mp4" type="video/mp4">
  Your browser does not support the video tag.
</video>

### Playback Speed
- Supported for Timeshift and Replay only; Live does not support speed
- Available speeds: 0.5×, 0.75×, 1.0×, 1.25×, 1.5×, 1.75×, 2.0×, 3.0×, 5.0×
- Audio pitch correction is enabled automatically to keep voices natural
- Consistent UI in fullscreen and window; adapts to dark/light themes

### Deinterlace
- Optimized for 1080i/720i interlaced video streams
- Three modes: Auto, Force On, Off
- Two algorithms: yadif (default), bwdif (better quality)
- Field parity settings: Auto, TFF, BFF
- Real-time preview when changing settings

### Channel Management
Grouping, Search, Favorites, and History (persisted locally).

### Channel List Badges (R/T)
- R: Catchup available; T: Timeshift available
- Hover tooltips localized (ZH/EN/ZH-TW/RU)
- Consistent style and layout in fullscreen and window

### Scheduled Reminder & Auto Play
- Future programs provide scheduling entry with separate "remind-only" and "auto-play-at-time" policies
- Reminder list supports single-instance management, checkbox batch deletion, Select All / Invert
- Unified entries from tray/dropdown/right-click; windows stay above the player in window/fullscreen states
- Due and success notifications do not steal focus, with consistent placement policies

### Minimal Mode
- Compact player window mode with top interactions and core playback controls
- Dedicated resize hit-testing, edge cursor hints, and drag/resize sync
- Synchronized state across minimal/window/fullscreen for badges, EPG, and channel list

### Optimization (FCC)
Fast Channel Change and UDP multicast optimization.

<video controls width="100%" poster="/logo.svg">
  <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
  Your browser does not support the video tag.
</video>

## Web Remote Control

Remotely control SrcBox through a browser.

[Detailed usage guide](./web-remote)

### Features
- **Playback Control**: Play, Pause, Stop, Seek Forward/Backward
- **Volume Control**: Volume slider, Mute toggle
- **Channel Switching**: Previous/Next channel, Channel list
- **Status View**: Current playback status, Program info
- **Password Protection**: Optional access password

### HTTP/RTSP Header Settings
Supports custom HTTP/HTTPS stream headers and RTSP transport settings.
[Detailed configuration guide](./http-headers)

## Fullscreen & Overlay
- **Double-click Fullscreen**: Toggle fullscreen by double-clicking the video area.
- **Overlay Bar**: Auto-shows at bottom on mouse move; supports controls & EPG toggle.
- **Side Drawer**: Auto-shows channel list (right) or EPG (left) on mouse hover near edges.

## Recording

- **Local Recording**: Record directly to local disk
- **WebDAV Upload**: Auto-upload recordings to WebDAV server
- **Real-time Recording**: Support for live recording mode
- **Upload Queue**: Upload task queue management

## v1.1.6 New Features

### Web Remote Control
- WebRemoteManager and WebRemoteServer implementation
- Control player through browser
- Real-time status updates, channel list, and EPG display
- Multi-language support (Chinese, English, Russian, etc.)
- Optional password protection

### Audio Settings
- Volume Gain: -200dB to +60dB
- Volume Max: 100% to 1000%
- Audio Delay: -100s to +100s
- Settings apply immediately

### Deinterlace
- Support for 1080i/720i interlaced video processing
- Three modes: Auto/Force On/Off
- Two algorithms: yadif/bwdif
- Real-time preview

### UI Improvements
- Optimized channel list refresh
- New mute function and volume slider style
- Resizable settings window

## Updates since v1.1.2

- **EPG Status Chips**: Chip-style badges on the right side of each program row
  - Live = red-filled chip; Replay = green-filled chip (auto adapts to dark/light themes)
  - Clicking the Live chip plays the current channel immediately
  - The current playing row is highlighted; when replaying, a green left stripe is shown for better visibility (especially in dark mode)
- **Reminders & Notifications**
  - Single-instance reminder list; centers to the player on first open; user-dragged position is respected afterward
  - Supports both "remind-only" and "auto-play" scheduling policies
  - Checkbox + Select All/Invert for batch deletion
  - Accessible from Tray / Dropdown / Right-click with consistent behavior
  - Due/success toasts don't steal focus (bottom-right vs. centered strategies are clearly separated)
- **M3U Management**
  - New "Manage M3U List" window (same style as reminders) with checkbox batch deletion and single edit
  - Tray entry; consistent entries from dropdown/right-click; visible in fullscreen above the player
- **System Tray**
  - Always-on icon; double-click to restore; menu includes Open/Reminders/Manage M3U/Settings/Exit
- **Close Confirmation**
  - Close "×/ESC" only dismisses dialog; "Yes" exits, "No" minimizes to tray (leave fullscreen first)
  - Three-line i18n text synchronized (ZH/EN/ZH-TW/RU)
- **Theme Sync**
  - Title bars for Reminders, M3U Manager, Reminder Dialog, and Exit Dialog apply theme at OnSourceInitialized for instant dark/light consistency
