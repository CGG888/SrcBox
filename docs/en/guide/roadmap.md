# Roadmap

We are committed to continuously improving the IPTV viewing experience.

## Future Plans

- [ ] **Smart Recommendations**: AI-powered program recommendations based on viewing habits
- [ ] **Cloud Recording (PVR)**: Record programs to remote storage
- [ ] **Advanced AV Experience**: HDR10+ dynamic metadata support, 8K 120fps decoding optimization
- [ ] **Interactive Features**: Voice comments (speech-to-text), low-latency cloud gaming entry
- [ ] **Copyright Protection**: Blockchain copyright verification (tamper-proof traceability)
- [ ] **Notification Animations**: Fade-in/slide-in reminder notifications, centered popup animations
- [ ] **Customizable Chips**: Customizable templates for live/replay chip icons/colors/corners

## Implemented Features

### Core Playback
- [x] **Timeshift**: Catchup-source based playback with real-time dragging, seek limited within program boundaries.
- [x] **EPG**: XMLTV (gz) parsing and display, with CCTV/Education channel type suffix support.
- [x] **Catchup**: Template-based automatic catchup URL generation.
- [x] **M3U Parsing**: Local/remote playlist and `#EXTINF` support, TXT format.
- [x] **M3U Binary Cache**: ETag/Last-Modified validated binary cache, millisecond-level loading.
- [x] **Channel Management**: Grouping, search, favorites, group sorting.

### Playback Optimization
- [x] **Live Optimization**: FCC fast channel change, UDP multicast, auto source switching.
- [x] **Hardware Decoding**: `d3d11va` enabled by default, multiple decoder options (D3D11VA/DXVA2/NVDEC/Software).
- [x] **Multi-Screen (v1.1.9)**: 4/6/9 screen simultaneous playback, 1-9 key selection, source switching.
- [x] **Web Remote Control**: Browser-based player control.

### Recording
- [x] **Local Recording**: Local recording, WebDAV upload, upload queue.
- [x] **Scheduled Recording (v1.1.9)**: Front/back scheduled recording modes, recording completion toast.

### Interface & Interaction
- [x] **Minimal Mode**: Compact window, top bar interaction, state sync.
- [x] **Scheduling**: Reminders, reminder list, auto-play policies.
- [x] **UI/UX**: Fullscreen overlay, side drawer, multi-language (ZH/EN/ZH-TW/RU).
- [x] **Keyboard Shortcuts**: Full keyboard shortcut support, shortcuts help window.
- [x] **Settings Window**: Resizable, optimized dark/light theme colors.
- [x] **Close Mode Memory (v1.1.9)**: Remember user choice (exit or minimize to tray).

### v1.1.9 New Features
- Multi-Screen: 4/6/9 screen playback, 1-9 key selection, ↑↓ switch channels, ←→ switch sources
- Tray Multi-Screen Menu: Add multi-screen submenu to tray context menu
- Scheduled Recording: Front/back recording modes, recording completion toast
- Decoder Selection: Auto/D3D11VA/DXVA2/NVDEC/Software, switchable during playback
- Shortcuts Help Window: ShortcutsWindow for keyboard shortcuts reference
- Menu System Refactoring: Classified by function domain, TXT format support
- Settings Window Refactoring: 12 tabs consolidated to 7
- Timeshift Improvements: Seek within program boundaries, continuous playback, auto-jump to next program
- M3U Dropdown Refresh: Auto-refresh after adding new source
- mpv Cache Optimization: Dynamic cache control based on recording state

### v1.1.2+ Updates
- EPG status chips: Live=red, Catchup=green; click to play
- Current indicator: full row highlight; green left stripe for catchup
- Reminders: single list; centered first open; checkboxes; tray/menu
- M3U management: batch delete; edit; tray entry
- System tray: Open/Reminders/M3U/Settings/Exit
- Close confirmation: ×/ESC=dialog; "No"=minimize to tray
- Theme sync: title bars apply instantly
- Speed control: 3×/5× with pitch correction for timeshift/catchup

### v1.1.6 Updates
- Web Remote Control: browser-based player control
- Audio Settings: volume gain, max volume, audio delay
- Deinterlace: 1080i/720i optimization
- UI improvements: resizable settings window

## Preview / Experimental

- [~] Reminder/notification animations: designed, future release
- [~] Clickable catchup chip: UX evaluation in progress
