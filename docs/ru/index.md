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
  - title: FCC Мгновенное переключение
    details: Миллисекундное переключение каналов, без долгого ожидания
    icon: ⚡
  - title: M3U + EPG
    details: Локальные/удалённые плейлисты M3U, полная телепрограмма XMLTV
    icon: 📺
  - title: Timeshift и Архив
    details: Перемотка прямого эфира, автогенерация ссылок на архив
    icon: ⏪
  - title: Запись по расписанию
    details: Передний/фоновый режим, синхронизация с WebDAV
    icon: 🎬
  - title: Мультиэкран
    details: Одновременный просмотр 4/6/9 экранов, быстрое переключение
    icon: 🖥️
  - title: Веб-пульт
    details: Управление плеером через браузер из любого места
    icon: 📱
  - title: Аппаратное декодирование
    details: D3D11VA/DXVA2/NVDEC/программное автоматическое переключение
    icon: 🔧
  - title: Управление каналами
    details: Группы, поиск, избранное, история — гибкое управление списком
    icon: 📋
---

<ClientOnly>
  <VideoShowcase />
</ClientOnly>

<ClientOnly>
  <ScreenshotGallery />
</ClientOnly>

<ClientOnly>
  <TechStack />
</ClientOnly>

<ClientOnly>
  <HelpSection />
</ClientOnly>

<div class="disclaimer">
  <strong>Отказ от ответственности:</strong> Видео, скриншоты и демо на этой странице предназначены только для демонстрации функций. <strong>Этот проект не предоставляет M3U плейлистов или данных каналов.</strong>
</div>

<style>
/* Hide volume controls for WebKit browsers */
video::-webkit-media-controls-volume-slider,
video::-webkit-media-controls-mute-button,
video::-webkit-media-controls-volume-control-hover-background,
video::-webkit-media-controls-volume-panel {
  display: none !important;
}

.disclaimer {
  max-width: 800px;
  margin: 60px auto;
  padding: 20px;
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-border);
  border-radius: 12px;
  font-size: 0.9rem;
  color: var(--vp-c-text-2);
  text-align: center;
}
</style>
