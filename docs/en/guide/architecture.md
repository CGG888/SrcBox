# Technical Architecture

This project is developed using **C# / WPF**. The core architecture is as follows:

- **UI Layer**: Built with WPF (ModernWpf), providing a smooth and modern user experience.
- **Architecture Layer**: `Architecture/` is split into Application / Platform / Presentation with modular playback/settings/sync flows.
- **Interop Layer**: Encapsulates libmpv calls via `MpvPlayer.cs` and `MpvPlayerEngineAdapter`.
- **Rendering Layer**: Uses `WindowsFormsHost` to host a Win32 window handle, embedding mpv's rendering output into the WPF interface to bypass WPF's native media element limitations.
- **Service Layer**:
  - `M3UParser`: High-performance regex-based parser supporting complex M3U extended tags.
  - `EpgService`: Asynchronous EPG loading and in-memory caching based on `XmlSerializer`.
  - `RecordingIndexService / UploadQueueService / WebDavClient`: recording index, upload queue, and remote storage pipeline.
  - `ReminderService`: reminder notification and scheduled autoplay orchestration.

## Project Structure

For detailed project structure, please refer to the [Full Architecture Document](/ARCHITECTURE.md).

```text
📂 SrcBox
├── 📂 Architecture    # Layered modules (Application/Platform/Presentation)
├── 📂 Services        # Core services (M3U/EPG/Recording/WebDAV/Notifications)
├── 📂 Controls        # Drawers and dialogs (EPG/Recording/Timeshift/Upload Queue)
├── 📂 Resources       # Localization and theme resources
├── 📂 Tests           # MSTest automation project
├── 📄 MainWindow.*.cs # Split main window logic
└── 📄 MpvPlayer.cs    # libmpv wrapper
```

## Core Business Modules

- **Scheduling**: `ReminderService` + `ScheduledReminder` supporting “remind-only / auto-play” policies.
- **Recording**: `MainWindowRecordingManager` handles start/stop, metadata writes, and list refresh.
- **Upload**: `UploadQueueService` + `WebDavClient` handle queueing, retry strategy, and remote path organization.
- **Minimal Mode**: coordinated by main window state and overlays, keeping badges/interactions in sync across window/fullscreen.

## libmpv Engine

This project depends on `libmpv-2.dll`.

- **Hardware Decoding**: `d3d11va` is enabled by default.
- **No Audio Issue**: Some IPTV sources have slow audio probing; `probesize=32` is set to speed up start, which might cause brief silence initially. This is an expected optimization strategy.
