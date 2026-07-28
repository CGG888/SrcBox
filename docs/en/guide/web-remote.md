# Web Remote Control

SrcBox provides Web Remote Control functionality, allowing you to remotely control the player through a browser, including playback control, volume adjustment, channel switching, and more.

## Features Overview

Web Remote is based on WebSocket and HTTP protocols, supporting the following features:

- **Playback Control**: Play, Pause, Stop
- **Progress Control**: Fast forward, Fast backward, Seek
- **Volume Control**: Volume increase/decrease, Mute
- **Channel Switching**: Previous/Next channel, Channel list
- **Status View**: Current playback status, Program info
- **Multi-language Support**: Chinese, English, Russian, etc.

## Enable Remote Control

1. Open SrcBox Settings window
2. Switch to **Web Remote** tab
3. Check **Enable Web Remote Control**
4. (Optional) Set access port (default 8899)
5. (Optional) Enable password protection, set access password
6. Click **Save** button

## Access Remote Control

After enabling, access in browser:

```
http://localhost:8899
```

For local access, use `localhost`. For LAN remote access, replace `localhost` with the IP address of the computer running SrcBox.

## Interface Description

### Status Display Area

- Current playback status (Live/Replay/Timeshift)
- Current channel name and Logo
- Current program name and playback time

### Playback Control Area

| Button | Function |
|--------|----------|
| Play/Pause | Toggle playback status |
| Stop | Stop playback |
| Fast Backward | Rewind 10 seconds |
| Fast Forward | Forward 10 seconds |

### Channel Control Area

| Button | Function |
|--------|----------|
| Previous Channel | Switch to previous channel |
| Next Channel | Switch to next channel |
| Channel List | Show/Hide channel list |

### Volume Control Area

| Control | Function |
|---------|----------|
| Volume Slider | Adjust volume |
| Mute Button | Toggle mute |

### Channel List

- Display all available channels
- Support search filtering
- Click channel name to switch playback

### EPG Program Guide

- Display program list for current channel
- Support page navigation for more programs
- Show program time and description

## Security Settings

### Password Protection

After enabling password protection, access to the remote control page requires password:

1. Check **Require Password** in settings
2. Set access password
3. Save settings

Access requires password to use remote control functionality.

### Notes

- By default, remote control only allows local access (localhost)
- For LAN access, ensure firewall allows the corresponding port
- It is recommended to enable password protection for security
- After changing port, refresh the browser page

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
