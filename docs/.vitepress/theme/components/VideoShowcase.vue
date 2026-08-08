<template>
  <section class="video-showcase">
    <h2 class="section-title">功能演示</h2>
    <p class="section-subtitle">探索 SrcBox 的核心功能</p>

    <div class="video-grid">
      <div class="video-item" v-for="video in videos" :key="video.id">
        <div class="video-wrapper" @click="openLightbox(video)">
          <div class="video-glow"></div>
          <video
            muted
            preload="metadata"
            playsinline
          >
            <source :src="video.src" type="video/mp4" />
            Your browser does not support the video tag.
          </video>
          <div class="play-overlay">
            <span class="play-icon">▶</span>
          </div>
        </div>
        <h3 class="video-title">{{ video.title }}</h3>
        <p class="video-description">{{ video.description }}</p>
      </div>
    </div>

    <!-- Video Lightbox -->
    <Teleport to="body">
      <div v-if="lightboxVisible" class="lightbox-overlay" @click="closeLightbox">
        <div class="lightbox-content" @click.stop>
          <button class="lightbox-close" @click="closeLightbox">×</button>
          <video ref="lightboxVideo" controls autoplay muted>
            <source :src="currentVideo.src" type="video/mp4" />
            Your browser does not support the video tag.
          </video>
          <p class="lightbox-caption">{{ currentVideo.title }}</p>
        </div>
      </div>
    </Teleport>
  </section>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

defineProps<{
  locale?: 'zh' | 'zh-TW' | 'en' | 'ru'
}>()

const videos = [
  {
    id: 'fcc',
    title: '毫秒级切台 (FCC)',
    description: '极致优化的快速换台体验，告别缓冲等待',
    src: '/screenshots/fast-zapping.mp4'
  },
  {
    id: 'catchup',
    title: 'Catchup / 节目回放',
    description: '基于模板自动生成回看地址，不错过精彩节目',
    src: '/screenshots/catchup.mp4'
  },
  {
    id: 'timeshift',
    title: 'Timeshift / 直播时移',
    description: '实时拖动进度条，随时回看直播历史',
    src: '/screenshots/timeshift.mp4'
  }
]

const lightboxVisible = ref(false)
const currentVideo = ref({ src: '', title: '' })
const lightboxVideo = ref<HTMLVideoElement | null>(null)

const openLightbox = (video: { src: string; title: string }) => {
  currentVideo.value = video
  lightboxVisible.value = true
  document.body.style.overflow = 'hidden'
}

const closeLightbox = () => {
  lightboxVisible.value = false
  document.body.style.overflow = ''
  if (lightboxVideo.value) {
    lightboxVideo.value.pause()
  }
}

const handleKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape' && lightboxVisible.value) {
    closeLightbox()
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown)
})
</script>

<style scoped>
.video-showcase {
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

.video-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 32px;
}

.video-item {
  text-align: center;
}

.video-wrapper {
  position: relative;
  border-radius: 12px;
  overflow: hidden;
  background: var(--vp-c-bg-mute);
  border: 2px solid var(--vp-c-border);
  transition: all 0.3s ease;
  cursor: pointer;
}

.video-wrapper video {
  display: block;
  width: 100%;
  position: relative;
  z-index: 0;
}

.video-glow {
  position: absolute;
  inset: -2px;
  border-radius: 12px;
  padding: 2px;
  background: linear-gradient(135deg, var(--vp-c-brand-1), var(--vp-c-accent), var(--vp-c-brand-1));
  -webkit-mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
  mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
  z-index: 1;
  opacity: 0.5;
  transition: opacity 0.3s ease;
}

.video-wrapper:hover .video-glow {
  opacity: 1;
}

.play-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.3s ease;
  z-index: 2;
}

.play-icon {
  font-size: 3rem;
  color: #ffffff;
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.video-wrapper:hover .play-overlay {
  opacity: 1;
}

.video-title {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--vp-c-text-1);
  margin: 16px 0 8px;
}

.video-description {
  font-size: 0.9rem;
  color: var(--vp-c-text-2);
  margin: 0;
  line-height: 1.5;
}

/* Lightbox Styles */
.lightbox-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.95);
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

.lightbox-content video {
  max-width: 100%;
  max-height: calc(90vh - 60px);
  border-radius: 8px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
  background: #000;
}

.lightbox-caption {
  color: #ffffff;
  margin-top: 16px;
  font-size: 1rem;
  text-align: center;
}

@media (max-width: 768px) {
  .video-showcase {
    padding: 48px 16px;
  }

  .section-title {
    font-size: 1.5rem;
  }

  .video-grid {
    grid-template-columns: 1fr;
    gap: 24px;
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
