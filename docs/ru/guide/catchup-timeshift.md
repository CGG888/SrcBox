# Архив (Catchup) и Timeshift (использование и настройка)

На этой странице описано, как пользоваться архивом/повтором (Catchup/Replay) и timeshift в плеере, а также как настраивать генерацию временных параметров через шаблон `catchup-source` в M3U (подходит и для HTTP unicast, и для RTSP unicast).

## Понятия и связь

- **Архив (Catchup/Replay)**: выберите прошедшую передачу в EPG; плеер подставит время начала/окончания в шаблон URL и запустит воспроизведение.
- **Timeshift**: во время просмотра “вживую” отмотайте ползунком назад; плеер подставит время курсора timeshift в шаблон URL и начнет воспроизведение.
- **Важно**: плеер не “понимает” приватные протоколы операторов. Он лишь заменяет временные плейсхолдеры в URL-шаблоне и воспроизводит получившийся URL.

### Скорость воспроизведения
- Выбор скорости доступен для Timeshift и Архива; для прямого эфира скорость не поддерживается
- Список скоростей: 0.5×, 0.75×, 1.0×, 1.25×, 1.5×, 1.75×, 2.0×, 3.0×, 5.0×
- При смене скорости включается коррекция высоты звука (pitch)

## Предварительные условия

Канал может поддерживать архив/timeshift, если верно хотя бы одно:

- В M3U для канала указан **`catchup-source`** (рекомендуется; настройка для каждого канала)
- В Настройках задан **глобальный шаблон URL** для архива/timeshift (используется, если `catchup-source` отсутствует)

Архив зависит от расписания:

- При наличии EPG: точные времена начала/окончания.
- Без EPG: архив может быть недоступен или работать неточно (зависит от источника).

## Действия в плеере (архив)

1. Включите канал (прямой эфир).
2. Откройте панель EPG (список программ слева).
3. Нажмите на программу, отмеченную как “Архив/Повтор” (обычно это передачи до текущего времени).
4. Плеер сформирует URL архива и начнет воспроизведение; статус сменится на “Архив”.

## Действия в плеере (timeshift)

1. Во время прямого эфира включите “Timeshift”.
2. Переместите ползунок назад на нужное время.
3. Отпустите мышь — плеер сформирует URL и начнет воспроизведение; статус станет “Timeshift”.
4. Выключите Timeshift, чтобы вернуться в прямой эфир.

## Формат M3U (рекомендуется: шаблон от канала)

Добавьте `catchup-source` в строку `#EXTINF`:

```m3u
#EXTINF:-1 tvg-id="CCTV1" tvg-name="CCTV1" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",CCTV1
https://example.com/live/index.m3u8
```

Примечания:

- `catchup-source` — это URL-шаблон архива. Он может совпадать с live URL или быть отдельной точкой архива.
- `catchup` (`default/append/shift`) — подсказка для плейлистов/плееров; в этом плеере ключевое — наличие `catchup-source` и корректная генерация URL.
- Если `catchup-source` содержит только параметры (например `?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}`), они дописываются к адресу трансляции канала (через `&`, если в адресе уже есть `?`). Значение `catchup="append"` задаёт это явно.
- Если live URL приходит из **rtp2httpd** (порт по умолчанию `5140` или путь с `/rtsp/`), настраивать **ничего не нужно**: плеер сам добавляет параметр архива `playseek=START-END` (в UTC, пока «маркер часового пояса rtp2httpd» выключен, и в локальном времени, когда он включён). Настроенный глобальный шаблон по-прежнему имеет приоритет над этим fallback.

## Временные плейсхолдеры (ядро настройки)

Эти плейсхолдеры можно использовать в `catchup-source` (или глобальных шаблонах):

### 1) Универсальные `${(b)FORMAT}` / `${(e)FORMAT}` (рекомендуется)

- `${(b)FORMAT}` — время начала
- `${(e)FORMAT}` — время окончания
- `FORMAT` — строка формата времени
- По умолчанию используется локальное время; если `FORMAT` заканчивается на `|UTC`, используется UTC

Пример (локальное время):

```text
?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}
```

Пример (UTC):

```text
?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}
```

### 2) `{utc:FORMAT}` / `{utcend:FORMAT}` (UTC)

```text
?begin={utc:yyyyMMddHHmmss}&end={utcend:yyyyMMddHHmmss}
```

### 3) `{start}` / `{end}` (фиксированный локальный формат)

```text
?start={start}&end={end}
```

### 4) Unix-метки (секунды) — начало/конец

Плеер поддерживает 10‑значные Unix-метки времени:

- Начало: `${timestamp}`, `{timestamp}`, `${(b)timestamp}`, `${(b)unix}`, `${(b)epoch}`
- Конец: `${end_timestamp}`, `{end_timestamp}`, `${(e)timestamp}`, `${(e)unix}`, `${(e)epoch}`
- Длительность (в секундах): `${duration}`, `{duration}`

Примеры распространённых интерфейсов:

```text
// 1) Параметры start/end
?start=${timestamp}&end=${end_timestamp}

// 2) playseek (начало-конец)
playseek=${(b)timestamp}-${(e)timestamp}

// 3) начало + длительность
?start=${timestamp}&duration=${duration}
```

Интеграция в M3U:

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&end=${end_timestamp}",Demo
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="Demo" catchup="append" catchup-source="https://example.com/live/index.m3u8?playseek=${(b)timestamp}-${(e)timestamp}",Demo
https://example.com/live/index.m3u8

#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?start=${timestamp}&duration=${duration}",Demo
https://example.com/live/index.m3u8
```

## Примеры шаблонов

### HTTP unicast (HLS m3u8, UTC + `T`)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/index.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}",Demo
https://example.com/live/index.m3u8
```

### RTSP unicast (PLTV `playseek`)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="append" catchup-source="rtsp://example.com/live.smil?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",Demo
rtsp://example.com/live.smil
```

### Универсально (starttime/endtime)

```m3u
#EXTINF:-1 tvg-name="Demo" catchup="default" catchup-source="https://example.com/live/stream?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}",Demo
https://example.com/live/stream
```

## Приоритет и переопределение

- `catchup-source` канала → формирует базовый URL и плейсхолдеры времени (рекомендуется настраивать для каждого канала).
- Шаблон Replay/Timeshift в Настройках → используется как fallback, если `catchup-source` отсутствует.
- Time Override (Настройки → Переопределение времени) → при включении изменяет только временную часть (схема/ключи/кодирование), не трогая домен/путь и нетемпоральные параметры. Применяется как к шаблонам канала, так и к fallback.
- Исключение: если в URL уже есть запрос `playseek`/`tvdr` (протокол rtp2httpd и подобные), его переписывает только схема «playseek»; остальные схемы оставляют URL без изменений, чтобы сервер не отдал прямой эфир вместо архива.
- Советы: сначала добейтесь корректного URL через шаблон канала/fallback, затем используйте Time Override для унификации формата времени (starttime/endtime, UTC, Unix секунды).

## Отсутствующие форматы / частные плейсхолдеры

- Если нужного формата нет в списке, либо источник использует частные плейсхолдеры/время в пути:
  - Включите Time Override и выберите ближайшую схему/кодирование;
  - Либо откройте issue с примерами; мы рассмотрим добавление пресетов или более гибкой настройки.
- Issues: https://github.com/CGG888/SrcBox/issues
## Дополнительные примеры плейсхолдеров (расширенные форматы)

- **RFC3339/ISO-8601 с часовым поясом**
  - UTC с `Z`:  
    `start=${(b)yyyy-MM-ddTHH:mm:ss|UTC}Z&end=${(e)yyyy-MM-ddTHH:mm:ss|UTC}Z`
  - Автоматический вывод `Z` или смещения (в зависимости от вида времени):  
    `start=${(b)yyyy-MM-ddTHH:mm:ssK}&end=${(e)yyyy-MM-ddTHH:mm:ssK}`
  - Явное смещение (например `+08:00`):  
    `start=${(b)yyyy-MM-ddTHH:mm:ss}(${(b)zzz})&end=${(e)yyyy-MM-ddTHH:mm:ss}(${(e)zzz})`

- **Только дата/только время**
  - `begin_date=${(b)yyyyMMdd}&begin_time=${(b)HHmmss}`
  - `end_date=${(e)yyyyMMdd}&end_time=${(e)HHmmss}`

- **Миллисекунды (если поддерживает источник)**
  - `start=${(b)yyyyMMddHHmmssfff}&end=${(e)yyyyMMddHHmmssfff}`

- **Эквивалентная запись через фигурные скобки (UTC)**
  - `begin={utc:yyyy-MM-ddTHH:mm:ss}&end={utcend:yyyy-MM-ddTHH:mm:ss}`

Примечания:
- `FORMAT` — это стандартные строки формата .NET; `|UTC` означает предварительную конвертацию в UTC.
- `K` выведет `Z` для UTC и смещение для локального времени; `zzz` всегда выводит смещение (например `+08:00`).
- Наличие миллисекунд, символа `T` и смещения зависит от протокола вашего источника.

## Связь с Настройками (рекомендация)

- Предпочитайте **`catchup-source` в M3U** для каждого канала.
- Шаблоны в Настройках используйте как **глобальный fallback**, когда `catchup-source` отсутствует.

## Диагностика

- Если в логе остаются `${(b)...}`/`{utc:...}` без замены, значит шаблон не применился или канал не использует `catchup-source`.
- Если URL выглядит правильно, но не воспроизводится — источник может требовать другие имена параметров/форматы/часовой пояс (локальный vs UTC).
