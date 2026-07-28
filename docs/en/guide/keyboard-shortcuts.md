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

## Interface Control

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Enter` | Toggle Fullscreen | Enter/exit fullscreen mode |
| `L` | Channel List | Show/hide channel list sidebar |
| `E` | EPG | Show/hide EPG program guide |

## Fullscreen Mode

| Shortcut | Function | Description |
|----------|----------|-------------|
| `Escape` | Exit Fullscreen | Return to window mode from fullscreen |

## Other

| Shortcut | Function | Description |
|----------|----------|-------------|
| `F1` | Debug Window | Open debug information window |

---

## Keyboard Layout

```
┌─────────────────────────────────────────────────────────────┐
│                        Keyboard Layout                       │
├─────────────┬───────────────────────────────────────────────┤
│  Top-Left   │  Function Keys: F1 (Debug)                    │
├─────────────┼───────────────────────────────────────────────┤
│  Left Side  │  ┌─────────────────────────────────────────┐  │
│             │  │        Playback Control                  │  │
│             │  │  Space: Play/Pause  S: Stop  M: Mute    │  │
│             │  └─────────────────────────────────────────┘  │
│             │  ┌─────────────────────────────────────────┐  │
│             │  │        Channel/Progress                  │  │
│             │  │  ↑/↓: Previous/Next Channel             │  │
│             │  │  ←/→: Switch Source or Rewind/FF        │  │
│             │  └─────────────────────────────────────────┘  │
│             │  ┌─────────────────────────────────────────┐  │
│             │  │        Interface Control                 │  │
│             │  │  Enter: Fullscreen  L: Channel  E: EPG  │  │
│             │  │  Esc: Exit Fullscreen                    │  │
│             │  └─────────────────────────────────────────┘  │
└─────────────┴───────────────────────────────────────────────┘
```

---

## FAQ

**Q: Why do the left/right keys sometimes switch channels and sometimes rewind/forward?**

A: The behavior of left/right keys is smart-switched:
- When watching live TV, left/right keys switch playback sources (suitable for multi-source channels)
- In timeshift or replay mode, left/right keys become rewind/fast forward

**Q: Do shortcuts work in both window and fullscreen modes?**

A: Yes, all shortcuts work in both window and fullscreen modes (except `Escape` which is only effective in fullscreen mode).
