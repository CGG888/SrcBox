# Настройка

## `user_settings.json`

Файл находится в директории программы и хранит пользовательские настройки.

```json
{
  "Hwdec": true,              // Аппаратное декодирование
  "FccPrefetchCount": 2,      // Количество FCC предварительной загрузки
  "EnableUdpOptimization": false,
  "SourceTimeoutSec": 3,      // Таймаут переключения источника (сек)
  "TimeshiftHours": 2,        // Длительность Timeshift (часы)
  "RecordingLocalDir": "recordings/{channel}",
  "Recording": {
    "Enabled": true,
    "SaveMode": "local_then_upload",
    "UploadMaxConcurrency": 1
  },
  "ScheduledReminders": [],
  "Language": "zh-CN",        // Язык интерфейса
  "ThemeMode": "System",      // Тема (Dark/Light/System)
  "ConfirmOnClose": true
}
```

## Параметры

- **Hwdec**: Включить аппаратное ускорение (по умолчанию `d3d11va`).
- **FccPrefetchCount**: Параллельная предварительная загрузка FCC, влияет на скорость и потребление ресурсов.
- **EnableUdpOptimization**: Включить оптимизацию UDP multicast.
- **SourceTimeoutSec**: Таймаут ожидания источника (сек).
- **TimeshiftHours**: Максимальная длительность перемотки назад (часы).
- **RecordingLocalDir**: Шаблон директории записи (поддерживает `{channel}`).
- **Recording.SaveMode**: Режим сохранения записи.
- **ScheduledReminders**: Список напоминаний с политиками "только напомнить/автозапуск".
- **Language**: Код языка интерфейса.
- **ThemeMode**: Тема приложения (Dark/Light/System).
- **ConfirmOnClose**: Показывать подтверждение при закрытии.

## Рекомендации

- Для сценариев напоминаний рекомендуется `ConfirmOnClose=true`.
- Для загрузки записанного рекомендуется настроить `Recording.UploadMaxConcurrency` под пропускную способность сети.
- При использовании удалённого хранилища настройте WebDAV.
