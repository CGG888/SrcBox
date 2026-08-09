# Development Guide

> For complete project architecture and technical details, please refer to the [Full Architecture Document](https://github.com/CGG888/SrcBox/blob/main/ARCHITECTURE.md).

## Prerequisites

- **OS**: Windows 10 / 11 (x64)
- **IDE**: Visual Studio 2022 or JetBrains Rider
- **SDK**: .NET 8.0 SDK
- **Dependency**: `libmpv-2.dll` (Must be manually placed in the output directory)

## Build & Run

```powershell
# Restore dependencies
dotnet restore

# Build (Debug)
dotnet build

# Run
dotnet run
```

> **Note**: Ensure `libmpv-2.dll` is placed in `bin\Debug\net8.0-windows\` before running, otherwise the app will crash.

## Troubleshooting

| Issue | Possible Cause | Solution |
| :--- | :--- | :--- |
| **Crash on startup** | Missing `libmpv-2.dll` | Download x64 dll and place in run dir |
| **Video but no audio** | Audio probe timeout | Normal optimization; try switching tracks or restarting |
| **EPG "No Data"** | Network or format issue | Check XMLTV URL accessibility and GZIP format |
| **Settings not saving** | Permission denied | Ensure write permission to app directory |

## Contribution

### Workflow

1. **Fork** the repository.
2. Create a feature branch: `git checkout -b feature/AmazingFeature`.
3. Commit changes: `git commit -m 'feat: Add some AmazingFeature'` (Follow [Conventional Commits](https://www.conventionalcommits.org/)).
4. Push branch: `git push origin feature/AmazingFeature`.
5. Submit a **Pull Request**.

### Code Style

- Follow existing C# style (K&R / Allman hybrid, see .editorconfig if available).
- Ensure UI changes adapt to both Dark and Light themes.

### Performance Benchmarks

- **CPU Usage**: < 15% @ 1080p (i5-8250U baseline).
- **Memory**: < 500MB during stable playback.
- **Startup**: < 2 seconds (cold start).
