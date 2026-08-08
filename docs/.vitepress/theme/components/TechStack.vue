<template>
  <section class="tech-stack-section">
    <h2 class="section-title">技术栈</h2>
    <p class="section-subtitle">现代化的技术选型</p>

    <div class="tech-grid">
      <div class="tech-card" v-for="tech in techs" :key="tech.name">
        <strong>{{ tech.name }}</strong>
        <span>{{ tech.desc }}</span>
      </div>
    </div>

    <div class="tech-architecture">
      <div class="arch-diagram">
        <div class="arch-layer" v-for="layer in architecture" :key="layer.name">
          <div class="arch-layer-name">{{ layer.name }}</div>
          <div class="arch-layer-items">
            <span class="arch-item" v-for="item in layer.items" :key="item">{{ item }}</span>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  locale?: 'zh' | 'zh-TW' | 'en' | 'ru'
}>()

const techs = [
  { name: 'libmpv', desc: '播放内核' },
  { name: 'WPF + ModernWpf', desc: 'UI 框架' },
  { name: 'C# (.NET 8)', desc: '开发语言' },
  { name: 'WebDAV', desc: '录播同步' },
  { name: 'WebSocket + HTTP', desc: '远程控制' },
  { name: 'VitePress', desc: '文档站点' }
]

const architecture = [
  {
    name: 'Presentation',
    items: ['MainWindow', 'ViewModels', 'MenuBuilder']
  },
  {
    name: 'Application',
    items: ['Settings', 'PlaybackSettings']
  },
  {
    name: 'Platform',
    items: ['MpvPlayer', 'MpvPlayerEngineAdapter']
  },
  {
    name: 'Services',
    items: ['M3U/EPG', 'Recording', 'WebDAV']
  }
]
</script>

<style scoped>
.tech-stack-section {
  padding: 80px 20px;
  max-width: 900px;
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

.tech-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 16px;
  margin-bottom: 48px;
}

.tech-card {
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-border);
  border-radius: 8px;
  padding: 16px;
  text-align: center;
  transition: all 0.2s ease;
}

.tech-card:hover {
  border-color: var(--vp-c-brand-1);
  box-shadow: var(--glow-brand);
  transform: translateY(-2px);
}

.tech-card strong {
  color: var(--vp-c-brand-1);
  display: block;
  margin-bottom: 4px;
  font-size: 1rem;
}

.tech-card span {
  color: var(--vp-c-text-2);
  font-size: 0.85rem;
}

.tech-architecture {
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-border);
  border-radius: 12px;
  padding: 24px;
}

.arch-diagram {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.arch-layer {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 16px;
  background: var(--vp-c-bg-mute);
  border-radius: 8px;
}

.arch-layer-name {
  color: var(--vp-c-brand-1);
  font-weight: 600;
  font-size: 0.9rem;
  min-width: 120px;
}

.arch-layer-items {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.arch-item {
  background: var(--vp-c-border);
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--vp-c-text-1);
}

@media (max-width: 768px) {
  .tech-stack-section {
    padding: 48px 16px;
  }

  .section-title {
    font-size: 1.5rem;
  }

  .tech-grid {
    grid-template-columns: repeat(2, 1fr);
    gap: 12px;
  }

  .arch-layer {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }

  .arch-layer-name {
    min-width: auto;
  }
}
</style>
