<template>
  <section class="help-section">
    <h2 class="section-title">{{ t('title') }}</h2>
    <p class="section-subtitle">{{ t('subtitle') }}</p>

    <div class="help-links">
      <a :href="guideLink" class="help-link">
        <span class="link-icon">📖</span>
        <div class="link-content">
          <span class="link-text">{{ t('guide') }}</span>
          <span class="link-desc">{{ t('guideDesc') }}</span>
        </div>
      </a>

      <a :href="shortcutsLink" class="help-link">
        <span class="link-icon">⌨️</span>
        <div class="link-content">
          <span class="link-text">{{ t('shortcuts') }}</span>
          <span class="link-desc">{{ t('shortcutsDesc') }}</span>
        </div>
      </a>

      <a href="https://github.com/CGG888/SrcBox/issues" class="help-link" target="_blank">
        <span class="link-icon">🐛</span>
        <div class="link-content">
          <span class="link-text">{{ t('issues') }}</span>
          <span class="link-desc">{{ t('issuesDesc') }}</span>
        </div>
      </a>

      <a href="https://github.com/CGG888/SrcBox/releases" class="help-link" target="_blank">
        <span class="link-icon">📦</span>
        <div class="link-content">
          <span class="link-text">{{ t('download') }}</span>
          <span class="link-desc">{{ t('downloadDesc') }}</span>
        </div>
      </a>
    </div>

    <div class="cta-section">
      <p class="cta-text">{{ t('cta') }}</p>
      <a :href="guideLink" class="cta-button">
        {{ t('ctaButton') }} →
      </a>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  locale?: 'zh' | 'zh-TW' | 'en' | 'ru'
}>()

const i18n = {
  'zh': {
    title: '获取帮助',
    subtitle: '开始使用 SrcBox',
    guide: '使用指南',
    guideDesc: '详细的功能介绍和配置说明',
    shortcuts: '键盘快捷键',
    shortcutsDesc: '快速上手操作指南',
    issues: '问题反馈',
    issuesDesc: '报告 Bug 或提出建议',
    download: '下载安装',
    downloadDesc: '获取最新版本',
    cta: '准备好开始使用了吗？',
    ctaButton: '访问文档'
  },
  'zh-TW': {
    title: '獲取幫助',
    subtitle: '開始使用 SrcBox',
    guide: '使用指南',
    guideDesc: '詳細的功能介紹和配置說明',
    shortcuts: '鍵盤快捷鍵',
    shortcutsDesc: '快速上手操作指南',
    issues: '問題反饋',
    issuesDesc: '報告 Bug 或提出建議',
    download: '下載安裝',
    downloadDesc: '獲取最新版本',
    cta: '準備好開始使用了嗎？',
    ctaButton: '訪問文檔'
  },
  'en': {
    title: 'Get Help',
    subtitle: 'Get started with SrcBox',
    guide: 'User Guide',
    guideDesc: 'Detailed feature guides and configuration',
    shortcuts: 'Keyboard Shortcuts',
    shortcutsDesc: 'Quick start guide',
    issues: 'Report Issues',
    issuesDesc: 'Report bugs or suggest features',
    download: 'Downloads',
    downloadDesc: 'Get the latest version',
    cta: 'Ready to get started?',
    ctaButton: 'View Documentation'
  },
  'ru': {
    title: 'Получить помощь',
    subtitle: 'Начните использовать SrcBox',
    guide: 'Руководство',
    guideDesc: 'Подробное описание функций и настройка',
    shortcuts: 'Горячие клавиши',
    shortcutsDesc: 'Краткое руководство',
    issues: 'Сообщить о проблемах',
    issuesDesc: 'Сообщить об ошибках или предложить функции',
    download: 'Скачать',
    downloadDesc: 'Получить последнюю версию',
    cta: 'Готовы начать?',
    ctaButton: 'Открыть документацию'
  }
}

const locale = computed(() => props.locale || 'zh')
const t = (key: string) => i18n[locale.value as keyof typeof i18n]?.[key as keyof typeof i18n['zh']] || i18n['zh'][key as keyof typeof i18n['zh']]

const guideLink = computed(() => {
  const links = {
    'zh': '/guide/',
    'zh-TW': '/zh-TW/guide/',
    'en': '/en/guide/',
    'ru': '/ru/guide/'
  }
  return links[locale.value as keyof typeof links] || '/guide/'
})

const shortcutsLink = computed(() => {
  const links = {
    'zh': '/guide/keyboard-shortcuts',
    'zh-TW': '/zh-TW/guide/keyboard-shortcuts',
    'en': '/en/guide/keyboard-shortcuts',
    'ru': '/ru/guide/keyboard-shortcuts'
  }
  return links[locale.value as keyof typeof links] || '/guide/keyboard-shortcuts'
})
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
