# Web Remote Control

SrcBox provides Web Remote Control functionality, allowing you to remotely control the player through a browser, including playback control, volume adjustment, channel switching, replay, scheduling recordings, and more.

## Features Overview

Web Remote is based on WebSocket and HTTP protocols, supporting the following features:

- **Playback Control**: Play, Pause, Stop
- **Progress Control**: Fast forward, Fast backward, Seek
- **Volume Control**: Volume adjustment, Mute
- **Channel Switching**: Previous/Next channel, Channel list, Channel search
- **Replay**: Timeshift replay, Program replay
- **Scheduling**: Reminder alerts, Scheduled recording (front/back)
- **Source Management**: Add, select, remove playback sources
- **Status View**: Current playback status, Program info, EPG guide
- **Multi-language Support**: Chinese, English, Russian, Traditional Chinese
- **Interface Switching**: One-click switch between the standard remote interface and the full interface, the choice is remembered automatically
- **Number Key Tuning**: The standard remote interface lets you enter a channel number directly to switch channels, and the channel list shows the matching numbers
- **QR Code Access**: Settings shows the access QR code and link; scan with your phone or copy the link to open
- **HTTPS Access**: One port serves both http and https, with a built-in self-signed certificate to install on your phone
- **Install to Home Screen**: Install the remote as a PWA and open it full screen in its own window

## Enable Remote Control

1. Open SrcBox Settings window
2. Switch to **Web Remote** tab (it is its own tab in Settings)
3. Check **Enable Web Remote Control**; it takes effect immediately, no restart required
4. (Optional) Set access port (default 8899)
5. (Optional) Enable password protection, set access password
6. Click **Save** button

## Access Remote Control

After enabling, access in browser:

```
http://localhost:8899
```

For local access, use `localhost`. For LAN remote access, replace `localhost` with the IP address of the computer running SrcBox, for example:

```
http://192.168.1.100:8899
```

The same port also serves HTTPS (the server tells the protocols apart from the first byte), so once the certificate is installed you can open `https://192.168.1.100:8899`; see [HTTPS and Certificates](#https-and-certificates) below.

### Scan to Open

After enabling, the **Web Remote** tab in the Settings window shows the current access address and QR code:

- Scan the QR code with your phone camera or browser to open the remote
- Click **Copy Link** to copy the access address
- When the certificate is ready, the QR code and link point to the **HTTPS address** (`https://<PC-IP>:<port>`; the phone must install the certificate before opening it for the first time); only when the certificate is unavailable do they fall back to the HTTP address
- The tab also shows the **certificate installation address**, the certificate status (including its expiry date) and the **Regenerate Certificate** button
- The QR code and link update automatically when the port changes; if no LAN address is found, check your network connection

### HTTPS and Certificates

Web Remote serves **HTTP and HTTPS on one port**: the same port accepts both `http://` and `https://` requests (the server looks at the first byte to tell whether the connection starts with a TLS handshake), so there is no separate HTTPS port to configure.

Why HTTPS:

- The browser only offers "Install app" (PWA) and service workers in a secure context (`https://` or `localhost`); opening the page as `http://192.168.x.x` on a phone cannot install the remote as an app
- Plain HTTP on the LAN can be read by other devices on the same network; HTTPS encrypts the traffic

On first use SrcBox **generates** a self-signed root certificate and a server certificate under `%LOCALAPPDATA%\SrcBox\webremote-certs` (the root is valid for 10 years; the server certificate for 397 days and renews 30 days before it expires). Nothing has to be created by hand.

When a phone opens `http://192.168.1.100:8899`, the **Install** button on the page reports the plain-http situation and offers the certificate downloads:

| Address | Download | Use |
|:---|:---|:---|
| `/cert` | `srcbox-ca.crt` (DER) | Install as a trusted CA certificate on Android and Windows |
| `/cert.pem` | `srcbox-ca.pem` | Text format fallback for Android builds that reject the `.crt` file |
| `/cert.mobileconfig` | `srcbox-ca.mobileconfig` | One-step configuration profile for iPhone |

**Installing the certificate on Android**: Settings → Security → Encryption & credentials → Install a certificate → CA certificate → pick the downloaded `srcbox-ca.crt` (use `.pem` when it cannot be read).

**Installing the certificate on iOS**: install the downloaded profile, then turn on full trust for the SrcBox certificate under Settings → General → About → Certificate Trust Settings.

Once the certificate is installed, use **Open over HTTPS** in the dialog (it goes to `https://192.168.1.100:8899/`), or simply edit the address bar to `https://`.

Skipping the certificate is fine too: on Android use the browser menu → "Add to Home screen" and then tap the fullscreen button (⛶) in the header; on iOS use Safari → Share → "Add to Home Screen" and the page opens full screen (iOS asks for no certificate). The only difference is that such a shortcut is not a real PWA install.

**Uninstalling the certificate**: on iPhone, remove the SrcBox profile under Settings → General → VPN & Device Management and then turn off full trust in Certificate Trust Settings; on Android, remove the SrcBox certificate under Settings → Security → Encryption & credentials → Trusted credentials → User; on Windows, delete SrcBox under Trusted Root Certification Authorities → Certificates in `certmgr.msc`. After uninstalling, switch back to `http://` or install the certificate again. The **Regenerate** button in the settings page issues a new root and server certificate, which immediately invalidates the old ones, so the phone has to install the certificate again.

### HTTPS availability and certificate renewal

- **Startup self-check**: when SrcBox starts it performs one TLS handshake against itself over the loopback address. Only if HTTPS really works does the page and the QR code hand out an `https://` address. If HTTPS cannot work in the current environment (for example the process is restricted by security policy, or the certificate directory is not writable), the page and the QR code fall back to `http://` while the certificate downloads (`/cert`, `/cert.pem`, `/cert.mobileconfig`) stay available — the phone will never be sent to an `https://` URL that cannot open.
- **Automatic renewal after a network change**: the server certificate carries the current adapter addresses in its SAN. When the IP changes (DHCP lease renewal, switching Wi-Fi or Ethernet), SrcBox **re-issues the server certificate** for the new address list without a restart, so the phone stops showing `ERR_CERT_COMMON_NAME_INVALID`; the root certificate is unchanged and the CA already installed on the phone keeps working.
- **Use HTTPS for the voice remote**: the phone microphone is only available in a secure context (`https://` or `localhost`). On `http://192.168.x.x` the page reports that it is not a secure context and the voice panel cannot record; install the certificate as described above and open the page over `https://`.

### Install to Home Screen (PWA)

The remote page is a PWA (`manifest.json` + service worker), so it can be installed to the phone's home screen and opened full screen like a native app:

- **Android (Chrome / Edge)**: in a secure context (https or localhost) the page shows an **Install** button; tap it and choose "Install app". If the browser offers no prompt, open the browser menu → "Install app" or "Add to Home screen"
- **iOS (Safari)**: tap Share → "Add to Home Screen"; that is the only install path on iOS
- The install entry disappears once the page runs in standalone mode, and stays hidden after "Don't show again"
- Installed, it opens in its own window without an address bar; the fullscreen button (⛶) in the header works on plain http as well

## Screenshot

| Standard remote · dark | Standard remote · light |
|:---:|:---:|
| ![Standard remote · dark](../../screenshots/web-remote-pad-dark.png) | ![Standard remote · light](../../screenshots/web-remote-pad-light.png) |
| **Full interface · dark** | **Full interface · light** |
| ![Full interface · dark](../../screenshots/web-remote-full-dark.png) | ![Full interface · light](../../screenshots/web-remote-full-light.png) |

## Interface Description

The button in the top-right corner switches between two interfaces, and the choice is remembered in the browser:

- **Standard Remote**: A phone-portrait-friendly remote layout (direction keys, number keys, volume keys, switch-source/mute)
- **Full Interface**: The full page with status display, playback control, channel list, EPG and source management (default)

### Standard Remote Pad

| Button | Function |
|--------|----------|
| ▲ Previous Channel | Switch to the previous channel |
| ▼ Next Channel | Switch to the next channel |
| ◀ Previous Source | Switch to the previous source |
| ▶ Next Source | Switch to the next source |
| OK | Toggle play/pause |
| Volume − / Volume + | Decrease / increase volume by 5 |
| Number keys 0-9 | Enter a channel number, up to 3 digits, auto-tunes 1.2 s after you stop typing |
| ⌫ | Clear the entered number |
| ✓ | Tune immediately |
| Switch Source / Mute | Switch playback source / toggle mute |
| Fullscreen / Close | Toggle fullscreen / exit the player |

The interface uses a Neumorphism style: it is light by default, and the 🌙 / ☀️ button in the top-right corner switches to the dark neumorphism style.

### Status Display Area

- Current playback mode (Live/Replay/Timeshift/Recording/Stopped)
- Current channel name and Logo
- Current program name and playback time

### Playback Control Area

| Button | Function |
|--------|----------|
| Fullscreen | Toggle fullscreen mode |
| Play | Start playback |
| Pause | Pause playback |
| Stop | Stop playback |
| Mute | Toggle mute |
| Previous Channel | Switch to previous channel |
| Next Channel | Switch to next channel |
| Switch Source | Switch playback source |
| Exit | Close player |
| Refresh | Refresh status data |

### Quick Control Area

| Button | Function |
|--------|----------|
| -10s | Rewind 10 seconds |
| +10s | Forward 10 seconds |
| Timeshift | Toggle timeshift mode on the current channel; the button state follows the player's `timeshiftEnabled` status |

### Volume Control Area

- Volume slider: Adjust volume level
- Mute button: Toggle mute

### Channel List

- Display all available channels and favorites
- Click channel name to switch playback
- Favorite channels displayed at top with star icon
- Each channel shows its channel number; numbers run continuously in playlist group order and never restart per group, matching the number keys of the standard remote pad one-to-one (favorites reuse the number of their group entry)

### EPG Program Guide

- Display program list for current channel (shows today's programs by default)
- Click program to enter replay mode
- Three action buttons supported:
  - **Watch** (Red): Jump straight to that channel and watch the live programme
  - **Replay** (Purple): Replay a past program on a channel that supports catch-up (catch-up source) or timeshift
  - **Reminder** (Blue): Schedule a reminder for the programme

### Program Badge Legend

| Badge | Color | Meaning |
|-------|-------|---------|
| LIVE | Red | Currently live |
| Replay | Purple | Currently replaying |
| Reminder | Blue `#1e90ff` | Reminder set |

### Voice Remote (offline)

The **microphone button** on the page opens the "Voice control" panel:

![Voice control panel](../../screenshots/web-remote-voice-light.png)

- Tap the big microphone button to start talking and tap it again to stop; **recording happens on the phone** (16 kHz mono) and the chunks are uploaded to the player
- The player recognises the audio with a **local offline model** (SenseVoice-Small, int8, about 228 MB, **no network**) — neither the audio nor the result leaves your machine
- The transcript and matching channels are echoed in the panel (say "3 台" and it lists `CCTV3`); the panel stays open after a command so you can keep talking
- The input box at the bottom of the panel sends a **typed command** with the same effect as speech

Things you can say:

| Phrase | Result |
|------|------|
| `湖南卫视` / `CCTV3` / `3 台` | Switch by channel name or number (homophones such as "2 太 / 2 胎" are understood too) |
| `下一个台` / `上一个台` | Next / previous channel |
| `换源` | Switch to the next source of the current channel |
| `音量 40` / `音量加` / `静音` | Set the volume, raise it, or mute |
| `快进 30 秒` / `后退 15 秒` | Relative seek (works while catching up or timeshifting) |
| `回看今天的新闻联播` | Find that programme in the current channel's guide and play it from the archive |
| `现在在播什么` | Read out what is on now |

The first run only needs the **Settings → Web remote → Offline voice recognition** switch: turning it on asks whether to download the model (about 228 MB) now and then opens a window showing the **phase (connecting / downloading / verifying), percentage, current file, speed and time left**, which can be stopped at any moment — whatever arrived is kept and the next attempt resumes it. Each file is checked against its published **SHA-256**, after which the model is loaded and put through one silent clip: the row says "the speech model is ready, self-check passed" only once that worked. The **Delete model** button next to it frees the disk and the memory and clears the switch. Wake words, custom phrase rules and the volume step live on the same page.

**Matching settings.** The *Match strictness* row on the same settings page decides how tolerant the sentence matching is:

- **Strictness**: `Loose` lists more candidates (it asks rather than guesses), `Standard` is the default, `Strict` keeps only close matches
- **Time hint**: how many minutes a programme may start from the hour you said for "replay the 8 pm news" to hit it (60 by default, so the window is 19:00-21:30; `0` means the exact hour)
- **Understand a spoken date**: with this on you can say "replay the news of October 13" or "last Friday's interview"
- **Play at once when a replay sentence matches one programme**: turn it off and even a single hit is confirmed first
- **Extra words to ignore**: words that are wrongly taken as part of a programme title, separated by `|`

**Voice packs (updated on their own).** Everything the app understands — wake words, switching, replay, the way dates and hours are spoken — lives in a **voice pack** that is decoupled from the app version. *Check* pulls the newest pack from GitHub (it is hashed with SHA-256 before it takes effect), *Import…* installs a local JSON file, *Remove* falls back to the built-in vocabulary, and dropping a `pack.json` into `%LOCALAPPDATA%\SrcBox\voice\` works too. A pack holds only words and regular expressions; every pattern runs with a 50 ms timeout and a broken pack falls back to the built-in tables.

> The phone microphone is only available in a secure context (`https://` or `localhost`): on `http://192.168.x.x` the page reports that it is not a secure context and recording cannot start — install the certificate as described under *HTTPS and Certificates* above and open the page over `https://`.

### Source Management (expandable)

- Display all saved playback sources
- The currently selected source is highlighted
- Support select, add, remove sources

### Reminder Management (expandable)

- Display all scheduled reminders
- Support cancel reminder
- Show channel, program name, time, action type

### Recording Management (expandable)

- Display all recording tasks
- Support stop in-progress recordings
- Support delete completed/failed recordings

## Security Settings

### Password Protection

After enabling password protection, access to the remote control page requires password:

1. Check **Require Password** in settings
2. Set access password
3. Save settings

Access requires password to use remote control functionality.

### Notes

- Once enabled, the service listens on **all network interfaces** (`0.0.0.0`), so any device on the same LAN can access it as long as it knows the address and port; **no password is set by default**, so it is recommended to tick "Require Password"
- For LAN access, ensure firewall allows the corresponding port
- It is recommended to enable password protection for security
- Plain `http://` traffic on the LAN can be read by other devices; install the certificate and switch to `https://` (see [HTTPS and Certificates](#https-and-certificates))
- Installing to the home screen needs a secure context (https or localhost); on plain http you can only "Add to Home screen"
- After changing port, refresh the browser page
- Settings has **Show channel list** and **Show EPG list** switches; switching one off hides the corresponding section on the remote page
- **Max EPG items** in settings limits how many programs the program guide returns per channel; the value range is 5–500 and the default is 50

## WebSocket API Reference

In addition to browser interface operations, you can also programmatically control via the WebSocket API.

### Connection

```javascript
const ws = new WebSocket('ws://localhost:8899');
```

### Authentication (when password required)

```json
// Send
{ "action": "auth", "password": "your_password" }

// Receive
{ "success": true, "requireAuth": true }
```

### General Response Format

All operations return JSON response on success, or `{ "error": "error message" }` / `{ "success": false }` on failure.

### API List

#### Playback Control

| Action | Parameters | Description |
|--------|------------|-------------|
| `play` | - | Play/Resume playback |
| `pause` | - | Pause playback |
| `stop` | - | Stop playback |
| `volume` | `volume` (0-100) | Set volume |
| `fullscreen` | - | Toggle fullscreen |
| `seek` | `seconds` (+/- number) | Seek seconds |

#### Channel Control

| Action | Parameters | Description |
|--------|------------|-------------|
| `channel` | `channelId` | Switch channel |

The Previous Channel / Next Channel buttons are implemented by the page: it works out the target channel id from the current channel list and then calls `channel`. The WebSocket API itself has no `prevChannel` / `nextChannel` actions (calling them returns `Unknown action`).

#### Speed Control

| Action | Parameters | Description |
|--------|------------|-------------|
| `setSpeed` | `speed` (0.5/1.0/2.0) | Set playback speed |
| `setTimeshift` | `enabled` (true/false) | Enable/disable timeshift |

#### Replay

| Action | Parameters | Description |
|--------|------------|-------------|
| `replayProgram` | `channelId`, `programTitle`, `start` (ISO), `end` (ISO) | Replay specified program |

#### Status Query

| Action | Returns | Description |
|--------|---------|-------------|
| `getStatus` | `{ playing, mode, channel, volume, ... }` | Get playback status |
| `getChannels` | `{ groups: [...], favorites: [...] }` | Get channel list |
| `getEpg` | `{ channelId, programs: [...] }` | Get program guide |
| `getSources` | `{ sources: [...] }` | Get source list |
| `getReminders` | `{ reminders: [...] }` | Get reminder list |
| `getRecordings` | `{ recordings: [...] }` | Get recording list |

#### Reminder Management

| Action | Parameters | Description |
|--------|------------|-------------|
| `addReminder` | `channelId`, `startAt` (ISO), `endTime` (ISO), `action`, `programTitle`, `preAlertSeconds`, `recordDurationMin` | Add reminder |
| `cancelReminder` | `id` | Cancel reminder |

#### Recording Management

| Action | Parameters | Description |
|--------|------------|-------------|
| `stopRecording` | `id` | Stop recording |
| `deleteRecording` | `id` | Delete recording |

#### Source Management

| Action | Parameters | Description |
|--------|------------|-------------|
| `selectSource` | `name` or `url` | Select source |
| `addSource` | `name`, `url` | Add source |
| `removeSource` | `name` or `url` | Remove source |
| `prevSource` | - | Previous source |
| `nextSource` | - | Next source |

#### Other

| Action | Parameters | Description |
|--------|------------|-------------|
| `exit` | - | Close player |
| `switchSource` | - | Switch source |

### Response Data Format

#### getStatus Response Example

```json
{
  "playing": true,
  "mode": "Replay",
  "modeText": "Replay",
  "channel": {
    "id": "CCTV1",
    "name": "CCTV1",
    "logo": "http://..."
  },
  "volume": 70,
  "muted": false,
  "speed": 1.0,
  "currentProgram": {
    "name": "Evening News",
    "start": "19:00",
    "end": "19:30",
    "isCurrent": true
  },
  "timeshiftEnabled": true,
  "timeshift": {
    "active": true,
    "cursor": "00:05:30",
    "range": "19:00 - 23:00"
  }
}
```

#### getEpg Response Example

```json
{
  "channelId": "CCTV1",
  "programs": [
    {
      "name": "News Broadcast",
      "start": "19:00",
      "end": "19:30",
      "startISO": "2026-08-16T11:00:00.000Z",
      "endISO": "2026-08-16T11:30:00.000Z",
      "isCurrent": true,
      "badge": "live",
      "badgeHtml": "<span class=\"epg-badge epg-badge-live\">LIVE</span>"
    }
  ]
}
```

## Troubleshooting

### Cannot Access Remote Control Page

1. Confirm Web Remote is enabled in settings
2. Check if port is occupied (default 8899)
3. Check firewall settings
4. Confirm SrcBox player is running

### Page Shows Blank

1. Try refreshing the page
2. Clear browser cache
3. Use another browser
4. Check if password protection is enabled

### Operations Not Responding

1. Confirm SrcBox player is playing
2. Check network connection
3. Try restarting SrcBox player

## Technical Implementation

Web Remote uses the following technologies:

- **WebSocket**: Real-time bidirectional communication for playback control commands
- **HTTP**: Status query and static resource services
- **JSON**: Data exchange format
- **Native HTML/CSS/JS**: No external dependencies

The server listens on port 8899 by default, and the Web interface is accessible through a browser without additional software.
