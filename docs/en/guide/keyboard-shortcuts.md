# Keyboard Shortcuts

SrcBox supports rich keyboard shortcuts for blind operation while watching live TV.

## Playback Control

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Space` | Play/Pause | Toggle playback state |
| `S` | Stop | Stop current playback |

## Channel Switching

| Shortcut | Function | Description |
|----------|----------|-------------|
| `↑` | Previous Channel | Switch to previous channel |
| `↓` | Next Channel | Switch to next channel |
| `←` | Previous Source | In live mode, switch to previous playback source |
| `→` | Next Source | In live mode, switch to next playback source |

## Timeshift & Replay

| Shortcut | Function | Description |
|----------|----------|-------------|
| `←` | Rewind | In timeshift/replay mode, rewind 10 seconds |
| `→` | Fast Forward | In timeshift/replay mode, fast forward 10 seconds |

> **Note**: Left/Right keys automatically switch behavior based on current playback mode:
> - Live mode: Switch playback source
> - Timeshift/Replay mode: Rewind/Fast forward

## Volume Control

| Shortcut | Function | Description |
|----------|----------|-------------|
| `M` | Mute | Toggle mute state |
| `=` / `+` | Volume Up | Increase volume |
| `-` / `Num-` | Volume Down | Decrease volume |

## Interface Control

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Enter` | Toggle Fullscreen | Enter/exit fullscreen mode |
| `L` | Channel List | Show/hide channel list sidebar |
| `Ctrl+Shift+L` | Minimal Mode | Toggle compact player window |
| `E` | EPG | Show/hide EPG program guide |

## Recording Control

| Shortcut | Function | Description |
|----------|----------|-------------|
| `R` | Start/Stop Recording | Control recording start or stop |

## Multi-Screen (v1.1.9 NEW)

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Ctrl+4` | 4-Screen | Open 4-screen multi-screen playback |
| `Ctrl+6` | 6-Screen | Open 6-screen multi-screen playback |
| `Ctrl+9` | 9-Screen | Open 9-screen multi-screen playback |
| `1-9` | Select Screen | Select corresponding screen in multi-screen |
| `↑/↓` | Switch Channel | Switch previous/next channel in multi-screen |
| `←/→` | Switch Source | Switch previous/next source in multi-screen |

## Fullscreen Mode

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Escape` | Exit Fullscreen | Return to window mode from fullscreen |

## Other

| Shortcut | Function | Description |
|----------|----------|-------------|
| `F1` | Debug Window | Open debug information window |
| `Ctrl+,` | Settings | Open settings window |
| `Ctrl+I` | About | Open about dialog |
| `Ctrl+/` | Shortcuts Help | Open keyboard shortcuts help window |

---

## Keyboard Layout

```
┌─────────────────────────────────────────────────────────────┐
│                        Keyboard Layout                       │
├─────────────┬───────────────────────────────────────────────┤
│  Top-Left   │  Function Keys: F1 (Debug)  Ctrl+I (About)   │
│             │  Ctrl+, (Settings)                            │
├─────────────┼───────────────────────────────────────────────┤
│  Left Side  │  ┌─────────────────────────────────────────┐  │
│             │  │        Playback Control                   │  │
│             │  │  Space: Play/Pause  S: Stop  M: Mute    │  │
│             │  │  =/-: Volume +/-  R: Recording          │  │
│             │  └─────────────────────────────────────────┘  │
│             │  ┌─────────────────────────────────────────┐  │
│             │  │        Channel/Progress                  │  │
│             │  │  ↑/↓: Previous/Next Channel             │  │
│             │  │  ←/→: Switch Source or Rewind/FF        │  │
│             │  └─────────────────────────────────────────┘  │
│             │  ┌─────────────────────────────────────────┐  │
│             │  │        Interface Control                  │  │
│             │  │  Enter: Fullscreen  L: Channel  E: EPG  │  │
│             │  │  Ctrl+Shift+L: Minimal Mode              │  │
│             │  │  Esc: Exit Fullscreen                    │  │
│             │  └─────────────────────────────────────────┘  │
│             │  ┌─────────────────────────────────────────┐  │
│             │  │        Multi-Screen (v1.1.9)            │  │
│             │  │  Ctrl+4/6/9: 4/6/9-Screen              │  │
│             │  │  1-9: Select Screen                      │  │
│             │  │  ↑/↓: Switch Channel  ←/→: Switch Src │  │
│             │  └─────────────────────────────────────────┘  │
└─────────────┴───────────────────────────────────────────────┘
```

---

## FAQ

**Q: Why do the left/right keys sometimes switch channels and sometimes rewind/forward?**

A: The behavior of left/right keys is smart-switched:
- When watching live TV, left/right keys switch playback sources (suitable for multi-source channels)
- In timeshift or replay mode, left/right keys become rewind/fast forward
- In multi-screen mode, left/right keys switch sources

**Q: Do shortcuts work in both window and fullscreen modes?**

A: Yes, all shortcuts work in both window and fullscreen modes (except `Escape` which is only effective in fullscreen mode).

**Q: How to use multi-screen shortcuts?**

A: After opening multi-screen, press 1-9 number keys to select the corresponding screen, then use ↑↓ to switch channels and ←→ to switch sources.

**Q: How to quickly open the settings window?**

A: Press `Ctrl+,` to quickly open the settings window.
