# SrcBox - Agent Instructions

## Build & Test

```powershell
dotnet build
dotnet test .\Tests\LibmpvIptvClient.Tests.csproj
```

## Architecture

- **Player core**: `MpvPlayer.cs` + `MpvPlayerEngineAdapter.cs` wrap libmpv-2.dll
- **Main ViewModel**: `Architecture/Presentation/Mvvm/MainWindow/MainShellViewModel.cs` - central state management
- **Menu building**: `Helpers/MenuBuilder.cs` - top-level menus; `MainWindowMenuManager.cs` - right-click menus
- **M3U/EPG services**: `Services/M3UCacheService.cs`, `Services/M3UParser.cs`, `Services/EpgService.cs`
- **Partial classes**: MainWindow logic split across `MainWindow.*.partial.cs` files

## Key Conventions

- Partial class pattern: `MainWindow.xaml` + `MainWindow.Core.partial.cs`, `MainWindow.EventsAndStartup.partial.cs`, etc.
- MenuBuilder callback pattern: `refreshChannels: null` means not bound; bind via `MainWindowMenuManager.CreateAppMenu()`
- M3U cache TTL controlled by `AppSettings.Current.M3uCacheTtlHours`
- Internationalization: `Resources/Strings.{locale}.xaml` - edit all 4 locale files

## Important Patterns

- `M3UCacheService.Instance.RemoveCache(url)` clears cache before forced refresh
- `MainShellViewModel.ForceRefreshChannels(url)` = clear cache + reload
- `LoadChannels(string url)` respects cache TTL; does NOT force download
- Volume settings (gain/max) applied in `MpvPlayer.Initialize()` - not dynamically changed
- mpv properties set via `SetString("property-name", value)` / `SetDouble()` / `SetFlag()`

## Common Tasks

**Add menu item**: Define callback in MenuBuilder.cs, bind in MainWindowMenuManager.cs

**Add shortcut**: Add to `MainWindowShortcutAction` enum + `ResolveAction()` + `ExecuteAction()`

**Add settings**: Add to `PlaybackSettings.cs`, bind in SettingsWindow or corresponding Settings*ViewModel
