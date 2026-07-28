# Roadmap

We are committed to continuously improving the IPTV viewing experience.

## Future Plans

- [ ] **Smart Recommendations**: AI-powered program recommendations based on viewing habits
- [ ] **Multi-View**: Support for multiple source picture-in-picture / quad-screen viewing
- [ ] **Cloud Recording (PVR)**: Record programs to remote storage
- [ ] **Advanced AV Experience**: HDR10+ dynamic metadata support, 8K 120fps decoding optimization
- [ ] **Interactive Features**: Voice comments (speech-to-text), low-latency cloud gaming entry
- [ ] **Copyright Protection**: Blockchain copyright verification (tamper-proof traceability)
- [ ] **Notification Animations**: Fade-in/slide-in reminder notifications, centered popup animations
- [ ] **Customizable Chips**:外观模板化 for live/replay chip icons/colors/corners

## Implemented Features

- [x] **Timeshift**: Catchup-source based playback with real-time dragging.
- [x] **EPG**: XMLTV (gz) parsing and display.
- [x] **Catchup**: Template-based automatic catchup URL generation.
- [x] **M3U Parsing**: Local/remote playlist and `#EXTINF` support.
- [x] **Channel Management**: Grouping, search, favorites.
- [x] **Live Optimization**: FCC fast channel change, UDP multicast, auto source switching.
- [x] **Hardware Decoding**: `d3d11va` enabled by default.
- [x] **Scheduling**: Reminders, reminder list, auto-play policies.
- [x] **Minimal Mode**: Compact window, top bar interaction, state sync.
- [x] **UI/UX**: Fullscreen overlay, side drawer, multi-language (ZH/EN/RU/TW).

- [x] **v1.1.2+ Updates**:
  - EPG status chips: Live=red, Catchup=green; click to play
  - Current indicator: full row highlight; green left stripe for catchup
  - Reminders: single list; centered first open; checkboxes; tray/menu
  - M3U management: batch delete; edit; tray entry
  - System tray: Open/Reminders/M3U/Settings/Exit
  - Close confirmation: ×/ESC=dialog; "No"=minimize to tray
  - Theme sync: title bars apply instantly
  - Speed control: 3×/5× with pitch correction for timeshift/catchup

- [x] **v1.1.6 Updates**:
  - Web Remote Control: browser-based player control
  - Audio Settings: volume gain, max volume, audio delay
  - Deinterlace: 1080i/720i optimization
  - UI improvements: resizable settings window

## Preview / Experimental

- [~] Reminder/notification animations: designed, future release
- [~] Clickable catchup chip: UX evaluation in progress
