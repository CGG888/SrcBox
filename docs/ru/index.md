---
layout: home

hero:
  name: "SrcBox"
  text: "Высокопроизводительный IPTV плеер для Windows"
  tagline: "На базе libmpv, с поддержкой EPG, Timeshift, Web-пульта и мгновенного переключения каналов."
  image:
    src: /logo.svg
    alt: Логотип SrcBox
  actions:
    - theme: brand
      text: Начать
      link: /ru/guide/
    - theme: alt
      text: GitHub
      link: https://github.com/CGG888/SrcBox

features:
  - title: Современный интерфейс
    details: Построен на WPF/ModernWpf, идеально сочетается с темной и светлой темами Windows 10/11.
    icon: 🎨
  - title: Высокая производительность
    details: Использует ядро libmpv, поддерживает аппаратное декодирование (d3d11va), малое потребление ресурсов.
    icon: 🚀
  - title: Мгновенное переключение (FCC)
    details: Технология FCC, оптимизированная для IPTV, мгновенное переключение каналов.
    icon: ⚡
  - title: Умный телегид (EPG)
    details: Полная поддержка XMLTV (gz) с автоматическим переключением по дням.
    icon: 📅
  - title: Timeshift и Архив
    details: Перемотка прямого эфира и автогенерация ссылок на архив.
    icon: ⏪
  - title: Планирование и напоминания
    details: Напоминания о программах и автозапуск, управление списком.
    icon: ⏰
  - title: Запись и загрузка
    details: Локальная запись, индекс записей, очередь загрузки на WebDAV.
    icon: ⬆️
  - title: Веб-пульт
    details: Управление плеером через браузер, воспроизведение, громкость, каналы.
    icon: 📱
  - title: Аудио настройки
    details: Усиление, макс. громкость, задержка аудио.
    icon: 🔊
  - title: Деинтерлейсинг
    details: Оптимизация для 1080i/720i чересстрочного видео с автоопределением.
    icon: 🖼️
  - title: Управление каналами
    details: Группировка, поиск, избранное и история с локальным сохранением.
    icon: 📺
  - title: Мультиязычность
    details: Поддержка упрощенного/традиционного китайского, английского, русского.
    icon: 🌍
---

<script setup>
import { onMounted } from 'vue'

onMounted(() => {
  // Custom logic
})
</script>

<style>
/* Hide volume controls for WebKit browsers (Chrome, Edge, Safari) */
video::-webkit-media-controls-volume-slider,
video::-webkit-media-controls-mute-button,
video::-webkit-media-controls-volume-control-hover-background,
video::-webkit-media-controls-volume-panel {
  display: none !important;
}
</style>

## О проекте

**SrcBox** — это высокопроизводительный, современный IPTV плеер, разработанный для платформы Windows.

Построен на мощном ядре воспроизведения **libmpv** в сочетании с современным интерфейсом **WPF**, обеспечивая плавный и стабильный просмотр прямых трансляций. Поддерживает M3U плейлисты, EPG, архив, таймшифт, запись и другие функции с глубокой оптимизацией для IPTV.

### Основные функции

- **libmpv ядро**: Высокая производительность, низкое потребление ресурсов, аппаратное ускорение
- **FCC быстрое переключение**: Миллисекундное переключение каналов для IPTV
- **EPG**: Полная поддержка XMLTV с интеллектуальным сопоставлением
- **Timeshift и Архив**: Перемотка эфира и автогенерация catchup
- **Веб-пульт**: Управление плеером через браузер
- **Аудио оптимизация**: Усиление, макс. громкость, задержка
- **Деинтерлейсинг**: Интеллектуальная обработка чересстрочного видео
- **WebDAV запись**: Локальная запись с облачной синхронизацией
- **Мультиязычность**: Китайский (упр./трад.), английский, русский

## Демонстрация функций

<div style="display: flex; flex-direction: column; gap: 60px; align-items: center; padding-bottom: 40px;">
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Мгновенное переключение (FCC)</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Миллисекундное сверхбыстрое переключение каналов</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/fast-zapping.mp4" type="video/mp4">
        Ваш браузер не поддерживает тег video.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Catchup / Архив</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Автогенерация ссылок на архив из шаблонов</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/catchup.mp4" type="video/mp4">
        Ваш браузер не поддерживает тег video.
      </video>
    </ClientOnly>
  </div>
  
  <div style="width: 100%; max-width: 800px; text-align: center;">
    <h3>Timeshift</h3>
    <p style="opacity: 0.6; margin-bottom: 10px;">Перемотка прямого эфира в реальном времени</p>
    <ClientOnly>
      <video controls muted preload="metadata" playsinline width="100%" style="border-radius: 12px; box-shadow: 0 8px 16px rgba(0,0,0,0.15); background-color: #000;">
        <source src="/screenshots/timeshift.mp4" type="video/mp4">
        Ваш браузер не поддерживает тег video.
      </video>
    </ClientOnly>
  </div>
</div>

## Скриншоты

<div style="display: flex; gap: 20px; overflow-x: auto; padding-bottom: 20px; justify-content: start;">
  <img src="/screenshots/main.png" alt="Главное окно" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/fullscreen-overlay.png" alt="Полноэкранный режим" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
  <img src="/screenshots/settings.png" alt="Настройки" style="height: 350px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
</div>

## Технологии

| Компонент | Технология |
|-----------|------------|
| Ядро воспроизведения | libmpv |
| UI фреймворк | WPF + ModernWpf |
| Язык | C# (.NET 8) |
| Синхронизация записи | WebDAV |
| Удаленное управление | WebSocket + HTTP |

## Помощь

- 📖 [Руководство](/ru/guide/) - Подробное описание функций и настроек
- ⌨️ [Горячие клавиши](/ru/guide/keyboard-shortcuts) - Краткое руководство по управлению
- 🐛 [Проблемы](https://github.com/CGG888/SrcBox/issues) - Сообщить об ошибке или предложить
- 📦 [Скачать](https://github.com/CGG888/SrcBox/releases) - Получить последнюю версию

>> **[Посетить официальную документацию](https://srcbox.top/ru)** <<

<div style="margin-top: 40px; padding-top: 20px; border-top: 1px solid var(--vp-c-divider); font-size: 14px; color: var(--vp-c-text-2);">
  <p><strong>Отказ от ответственности:</strong> Видео, скриншоты и демо на этой странице предназначены только для демонстрации функций. <strong>Этот проект не предоставляет M3U плейлистов или данных каналов.</strong></p>
</div>
