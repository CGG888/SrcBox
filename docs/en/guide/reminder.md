# Reminder Feature

SrcBox provides powerful reminder functionality, supporting reminder notifications, scheduled auto-play, and scheduled recording - your essential companion for watching IPTV live broadcasts.

## Overview

| Feature | Description |
|---------|-------------|
| **Reminder Notification** | Notify before program starts, no auto-play |
| **Scheduled Auto-Play** | Auto-switch to target channel at scheduled time |
| **Scheduled Recording** | Auto-start recording at scheduled time (Front/Back mode) |

---

## Reminder Notification

Pop-up notification before program starts, user can choose to watch or ignore.

### Features
- Advance notification (customizable time)
- Toast notification in bottom-right corner, no focus steal
- Remind-only mode supported

### Usage
1. Select target program in EPG
2. Right-click → "Schedule"
3. Choose "Remind Only" mode
4. Wait for notification

---

## Scheduled Auto-Play

Automatically switch to target channel and play at scheduled time.

### Features
- Auto-play target channel at time
- Support starting from timeshift position
- Auto-switch to target channel

### Usage
1. Select target program in EPG
2. Right-click → "Schedule"
3. Choose "Auto-Play" mode
4. Auto-switch and play at scheduled time

---

## Scheduled Recording (v1.1.9 NEW)

Auto-start recording at scheduled time, supporting front and back modes.

### Front Recording

Screen jumps to the recording channel during recording.

| Feature | Description |
|---------|-------------|
| Screen Jump | Switch to recording channel |
| Real-time Preview | See recording画面 |
| Custom Duration | Set recording duration, auto-stop at time |
| Timer Display | Show elapsed recording time |

### Back Recording

Keeps current playback channel without jumping.

| Feature | Description |
|---------|-------------|
| No Interruption | Keep current playback |
| Background Run | Recording runs in background |
| Auto-Stop | Duration-based auto-stop supported |
| Completion Notification | Toast notification when recording completes |

### Recording Completion Notification

- Toast notification when recording completes
- Auto-hide after 5 seconds
- Differentiated notifications for front/back recording

### Usage

1. Select target program in EPG
2. Right-click → "Schedule"
3. Choose "Front Recording" or "Back Recording" mode
4. Optionally set recording duration
5. Auto-start recording at scheduled time

---

## Reminder List

Manage all created reminders.

### Features
- Single instance management window
- Checkbox batch delete
- Select All / Invert
- Status filter dropdown (v1.1.9 NEW)
- Refresh button (v1.1.9 NEW)
- Window stays above player

### Entry Points
- Tray right-click menu → Reminders
- Dropdown menu → Reminder Management
- Right-click menu → Reminder List

---

## Keyboard Shortcuts

| Shortcut | Function |
|----------|----------|
| `R` | Start/Stop Recording |

---

## Related Settings

Reminder-related settings can be configured in Settings window:
- Advance reminder time
- Default reminder mode
- Recording save path
- Recording completion notification method
