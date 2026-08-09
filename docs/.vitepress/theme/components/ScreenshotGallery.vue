<template>
  <section class="screenshot-gallery-section">
    <h2 class="section-title">{{ t('title') }}</h2>
    <p class="section-subtitle">{{ t('subtitle') }}</p>

    <div class="gallery-grid">
      <div class="screenshot-item" v-for="(screenshot, index) in screenshots" :key="index">
        <div class="screenshot-frame" @click="openLightbox(screenshot)">
          <img :src="screenshot.src" :alt="screenshot.alt" loading="lazy" />
          <div class="screenshot-overlay">
            <span class="overlay-icon">🔍</span>
          </div>
        </div>
        <p class="screenshot-caption">{{ screenshot.alt }}</p>
      </div>
    </div>

    <!-- Lightbox Modal -->
    <Teleport to="body">
      <div v-if="lightboxVisible" class="lightbox-overlay" @click="closeLightbox">
        <div class="lightbox-content" @click.stop>
          <button class="lightbox-close" @click="closeLightbox">×</button>
          <img :src="currentImage.src" :alt="currentImage.alt" />
          <p class="lightbox-caption">{{ currentImage.alt }}</p>
        </div>
      </div>
    </Teleport>
  </section>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

const props = defineProps<{
  locale?: 'zh' | 'zh-TW' | 'en' | 'ru'
}>()

const i18n = {
  'zh': {
    title: '界面预览',
    subtitle: '现代化的用户界面设计',
    main: '主界面',
    multiscreen: '多屏播放',
    settings: '设置窗口'
  },
  'zh-TW': {
    title: '介面預覽',
    subtitle: '現代化的使用者介面設計',
    main: '主介面',
    multiscreen: '多屏播放',
    settings: '設定視窗'
  },
  'en': {
    title: 'Interface Preview',
    subtitle: 'Modern user interface design',
    main: 'Main Interface',
    multiscreen: 'Multi-Screen',
    settings: 'Settings Window'
  },
  'ru': {
    title: 'Превью интерфейса',
    subtitle: 'Современный дизайн пользовательского интерфейса',
    main: 'Главный интерфейс',
    multiscreen: 'Мультиэкран',
    settings: 'Окно настроек'
  }
}

const locale = computed(() => props.locale || 'zh')
const t = (key: string) => i18n[locale.value as keyof typeof i18n]?.[key as keyof typeof i18n['zh']] || i18n['zh'][key as keyof typeof i18n['zh']]

const screenshots = computed(() => [
  {
    src: '/screenshots/main.png',
    alt: t('main')
  },
  {
    src: '/screenshots/multiscreen.png',
    alt: t('multiscreen')
  },
  {
    src: '/screenshots/settings.png',
    alt: t('settings')
  }
])

const lightboxVisible = ref(false)
const currentImage = ref({ src: '', alt: '' })

const openLightbox = (screenshot: { src: string; alt: string }) => {
  currentImage.value = screenshot
  lightboxVisible.value = true
  document.body.style.overflow = 'hidden'
}

const closeLightbox = () => {
  lightboxVisible.value = false
  document.body.style.overflow = ''
}
</script>

<style scoped>
.screenshot-gallery-section {
  padding: 80px 20px;
  max-width: 1200px;
  margin: 0 auto;
}

.section-title {
  font-size: 2rem;
  font-weight: 700;
  color: var(--vp-c-text-1);
  text-align: center;
  margin: 0 0 12px;
}

.section-subtitle {
  color: var(--vp-c-text-2);
  text-align: center;
  margin: 0 0 48px;
}

.gallery-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 24px;
}

.screenshot-item {
  display: flex;
  flex-direction: column;
}

.screenshot-frame {
  position: relative;
  border-radius: 12px;
  overflow: hidden;
  border: 2px solid var(--vp-c-border);
  background: var(--vp-c-bg-soft);
  transition: all 0.3s ease;
  aspect-ratio: 16 / 10;
  cursor: pointer;
}

.screenshot-frame img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.3s ease;
}

.screenshot-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.3s ease;
}

.overlay-icon {
  font-size: 2rem;
}

.screenshot-frame:hover {
  transform: translateY(-4px);
  border-color: var(--vp-c-brand-1);
  box-shadow: var(--glow-brand), var(--vp-shadow-1);
}

.screenshot-frame:hover img {
  transform: scale(1.05);
}

.screenshot-frame:hover .screenshot-overlay {
  opacity: 1;
}

.screenshot-caption {
  text-align: center;
  color: var(--vp-c-text-2);
  font-size: 0.9rem;
  margin: 12px 0 0;
}

/* Lightbox Styles */
.lightbox-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.9);
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px;
  animation: fadeIn 0.2s ease;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.lightbox-content {
  position: relative;
  max-width: 90vw;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.lightbox-close {
  position: absolute;
  top: -40px;
  right: 0;
  background: none;
  border: none;
  color: #ffffff;
  font-size: 2rem;
  cursor: pointer;
  padding: 8px;
  line-height: 1;
  opacity: 0.8;
  transition: opacity 0.2s;
}

.lightbox-close:hover {
  opacity: 1;
}

.lightbox-content img {
  max-width: 100%;
  max-height: calc(90vh - 60px);
  object-fit: contain;
  border-radius: 8px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
}

.lightbox-caption {
  color: #ffffff;
  margin-top: 16px;
  font-size: 1rem;
  text-align: center;
}

@media (max-width: 900px) {
  .gallery-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 600px) {
  .screenshot-gallery-section {
    padding: 48px 16px;
  }

  .section-title {
    font-size: 1.5rem;
  }

  .gallery-grid {
    grid-template-columns: 1fr;
    gap: 16px;
  }

  .screenshot-frame {
    aspect-ratio: 16 / 9;
  }

  .lightbox-overlay {
    padding: 20px;
  }

  .lightbox-close {
    top: -36px;
    font-size: 1.5rem;
  }
}
</style>
