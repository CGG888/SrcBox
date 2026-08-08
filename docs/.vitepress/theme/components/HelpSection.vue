<template>
  <section class="help-section">
    <h2 class="section-title">获取帮助</h2>
    <p class="section-subtitle">开始使用 SrcBox</p>

    <div class="help-links">
      <a href="/guide/" class="help-link">
        <span class="link-icon">📖</span>
        <div class="link-content">
          <span class="link-text">使用指南</span>
          <span class="link-desc">详细的功能介绍和配置说明</span>
        </div>
      </a>

      <a href="/guide/keyboard-shortcuts" class="help-link">
        <span class="link-icon">⌨️</span>
        <div class="link-content">
          <span class="link-text">键盘快捷键</span>
          <span class="link-desc">快速上手操作指南</span>
        </div>
      </a>

      <a href="https://github.com/CGG888/SrcBox/issues" class="help-link" target="_blank">
        <span class="link-icon">🐛</span>
        <div class="link-content">
          <span class="link-text">问题反馈</span>
          <span class="link-desc">报告 Bug 或提出建议</span>
        </div>
      </a>

      <a href="https://github.com/CGG888/SrcBox/releases" class="help-link" target="_blank">
        <span class="link-icon">📦</span>
        <div class="link-content">
          <span class="link-text">下载安装</span>
          <span class="link-desc">获取最新版本</span>
        </div>
      </a>
    </div>

    <div class="cta-section">
      <p class="cta-text">准备好开始使用了吗？</p>
      <a href="/guide/" class="cta-button">
        访问文档 →
      </a>
    </div>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  locale?: 'zh' | 'zh-TW' | 'en' | 'ru'
}>()
</script>

<style scoped>
.help-section {
  padding: 80px 20px;
  max-width: 900px;
  margin: 0 auto;
  text-align: center;
}

.section-title {
  font-size: 2rem;
  font-weight: 700;
  color: var(--vp-c-text-1);
  margin: 0 0 12px;
}

.section-subtitle {
  color: var(--vp-c-text-2);
  margin: 0 0 48px;
}

.help-links {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 48px;
  text-align: left;
}

.help-link {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px;
  background: var(--vp-c-bg-soft);
  border: 2px solid var(--vp-c-border);
  border-radius: 12px;
  text-decoration: none;
  transition: all 0.2s ease;
}

.help-link:hover {
  border-color: var(--vp-c-brand-1);
  box-shadow: var(--glow-brand);
  transform: translateY(-2px);
}

.link-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.link-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.link-text {
  color: var(--vp-c-text-1);
  font-weight: 600;
  font-size: 1rem;
}

.link-desc {
  color: var(--vp-c-text-2);
  font-size: 0.85rem;
  line-height: 1.4;
}

.cta-section {
  padding: 32px;
  background: var(--vp-c-brand-soft);
  border: 1px solid var(--vp-c-brand-1);
  border-radius: 12px;
}

.cta-text {
  color: var(--vp-c-text-1);
  font-size: 1.1rem;
  margin: 0 0 16px;
}

.cta-button {
  display: inline-block;
  padding: 12px 24px;
  background: linear-gradient(135deg, var(--vp-c-brand-1), var(--vp-c-brand-3));
  color: #ffffff;
  text-decoration: none;
  border-radius: 8px;
  font-weight: 600;
  transition: all 0.2s ease;
}

.cta-button:hover {
  background: linear-gradient(135deg, var(--vp-c-brand-light), var(--vp-c-brand-1));
  box-shadow: var(--glow-brand);
  transform: translateY(-1px);
}

@media (max-width: 768px) {
  .help-section {
    padding: 48px 16px;
  }

  .section-title {
    font-size: 1.5rem;
  }

  .help-links {
    grid-template-columns: 1fr;
  }

  .help-link {
    flex-direction: row;
    align-items: center;
  }

  .link-content {
    flex-direction: row;
    gap: 12px;
    align-items: center;
  }

  .link-desc {
    display: none;
  }
}
</style>
