import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  title: "SrcBox",
  description: "A modern IPTV player based on libmpv",
  base: "/",
  
  // Favicon configuration
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }]
  ],
  
  // Theme related configurations
  themeConfig: {
    logo: '/logo.svg',
    siteTitle: 'SrcBox',
    socialLinks: [
      { icon: 'github', link: 'https://github.com/CGG888/SrcBox' }
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024-present CGG888'
    },
    search: {
      provider: 'local'
    }
  },

  // Internationalization
  locales: {
    root: {
      label: '简体中文',
      lang: 'zh',
      link: '/',
      themeConfig: {
        nav: [
          { text: '首页', link: '/' },
          { text: '指南', link: '/guide/' },
          { text: '下载', link: 'https://github.com/CGG888/SrcBox/releases' },
          { text: '问题反馈', link: 'https://github.com/CGG888/SrcBox/issues' }
        ],
        sidebar: [
          {
            text: '指南',
            items: [
              { text: '项目介绍', link: '/guide/introduction' },
              { text: '功能特性', link: '/guide/features' },
              { text: 'Web 远程控制', link: '/guide/web-remote' },
              { text: 'EPG 节目单', link: '/guide/epg' },
              { text: 'HTTP/RTSP Header', link: '/guide/http-headers' },
              { text: '回看与时移', link: '/guide/catchup-timeshift' },
              { text: '键盘快捷键', link: '/guide/keyboard-shortcuts' },
              { text: '技术架构', link: '/guide/architecture' },
              { text: '开发指南', link: '/guide/development' },
              { text: '配置说明', link: '/guide/configuration' },
              { text: '国际化', link: '/i18n' },
              { text: '路线图', link: '/guide/roadmap' },
              { text: '常见问题', link: '/guide/faq' }
            ]
          }
        ],
        outline: {
          label: '页面导航'
        },
        docFooter: {
          prev: '上一页',
          next: '下一页'
        },
        lastUpdated: {
          text: '最后更新于'
        },
        editLink: {
          pattern: 'https://github.com/CGG888/SrcBox/edit/main/docs/:path',
          text: '在 GitHub 上编辑此页'
        }
      }
    },
    'zh-TW': {
      label: '繁體中文',
      lang: 'zh-TW',
      link: '/zh-TW/',
      title: 'SrcBox',
      description: '以 libmpv 為核心的現代 IPTV 播放器',
      themeConfig: {
        nav: [
          { text: '首頁', link: '/zh-TW/' },
          { text: '指南', link: '/zh-TW/guide/' },
          { text: '下載', link: 'https://github.com/CGG888/SrcBox/releases' },
          { text: '問題反饋', link: 'https://github.com/CGG888/SrcBox/issues' }
        ],
        sidebar: [
          {
            text: '指南',
            items: [
              { text: '專案介紹', link: '/zh-TW/guide/introduction' },
              { text: '功能特性', link: '/zh-TW/guide/features' },
              { text: 'Web 遠程控制', link: '/zh-TW/guide/web-remote' },
              { text: 'EPG 節目單', link: '/zh-TW/guide/epg' },
              { text: 'HTTP/RTSP Header', link: '/zh-TW/guide/http-headers' },
              { text: '回看與時移', link: '/zh-TW/guide/catchup-timeshift' },
              { text: '鍵盤快捷鍵', link: '/zh-TW/guide/keyboard-shortcuts' },
              { text: '技術架構', link: '/zh-TW/guide/architecture' },
              { text: '開發指南', link: '/zh-TW/guide/development' },
              { text: '配置說明', link: '/zh-TW/guide/configuration' },
              { text: '國際化', link: '/zh-TW/i18n' },
              { text: '路線圖', link: '/zh-TW/guide/roadmap' },
              { text: '常見問題', link: '/zh-TW/guide/faq' }
            ]
          }
        ],
        outline: {
          label: '頁面導航'
        },
        footer: {
          message: '以 MIT 授權條款釋出。',
          copyright: 'Copyright © 2024-present CGG888'
        },
        docFooter: {
          prev: '上一頁',
          next: '下一頁'
        },
        lastUpdated: {
          text: '最後更新於'
        },
        darkModeSwitchLabel: '外觀',
        lightModeSwitchTitle: '切換為淺色模式',
        darkModeSwitchTitle: '切換為深色模式',
        sidebarMenuLabel: '選單',
        returnToTopLabel: '回到頂部',
        langMenuLabel: '語言',
        notFound: {
          title: '找不到頁面',
          quote: '您要找的頁面不存在或已被移除。',
          linkLabel: '前往首頁',
          linkText: '回到首頁'
        },
        editLink: {
          pattern: 'https://github.com/CGG888/SrcBox/edit/main/docs/:path',
          text: '在 GitHub 上編輯此頁'
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en',
      link: '/en/',
      themeConfig: {
        nav: [
          { text: 'Home', link: '/en/' },
          { text: 'Guide', link: '/en/guide/' },
          { text: 'Download', link: 'https://github.com/CGG888/SrcBox/releases' },
          { text: 'Issues', link: 'https://github.com/CGG888/SrcBox/issues' }
        ],
        sidebar: [
          {
            text: 'Guide',
            items: [
              { text: 'Introduction', link: '/en/guide/introduction' },
              { text: 'Features', link: '/en/guide/features' },
              { text: 'Web Remote', link: '/en/guide/web-remote' },
              { text: 'EPG', link: '/en/guide/epg' },
              { text: 'HTTP/RTSP Header', link: '/en/guide/http-headers' },
              { text: 'Catchup & Timeshift', link: '/en/guide/catchup-timeshift' },
              { text: 'Keyboard Shortcuts', link: '/en/guide/keyboard-shortcuts' },
              { text: 'Architecture', link: '/en/guide/architecture' },
              { text: 'Development', link: '/en/guide/development' },
              { text: 'Configuration', link: '/en/guide/configuration' },
              { text: 'Internationalization', link: '/en/i18n' },
              { text: 'Roadmap', link: '/en/guide/roadmap' },
              { text: 'FAQ', link: '/en/guide/faq' }
            ]
          }
        ],
        editLink: {
          pattern: 'https://github.com/CGG888/SrcBox/edit/main/docs/:path',
          text: 'Edit this page on GitHub'
        }
      }
    },
    ru: {
      label: 'Русский',
      lang: 'ru',
      link: '/ru/',
      title: 'SrcBox',
      description: 'Современный IPTV-плеер на базе libmpv',
      themeConfig: {
        nav: [
          { text: 'Главная', link: '/ru/' },
          { text: 'Руководство', link: '/ru/guide/' },
          { text: 'Скачать', link: 'https://github.com/CGG888/SrcBox/releases' },
          { text: 'Проблемы', link: 'https://github.com/CGG888/SrcBox/issues' }
        ],
        sidebar: [
          {
            text: 'Руководство',
            items: [
              { text: 'Обзор', link: '/ru/guide/introduction' },
              { text: 'Возможности', link: '/ru/guide/features' },
              { text: 'Веб-пульт', link: '/ru/guide/web-remote' },
              { text: 'EPG', link: '/ru/guide/epg' },
              { text: 'HTTP/RTSP Header', link: '/ru/guide/http-headers' },
              { text: 'Архив и Timeshift', link: '/ru/guide/catchup-timeshift' },
              { text: 'Горячие клавиши', link: '/ru/guide/keyboard-shortcuts' },
              { text: 'Архитектура', link: '/ru/guide/architecture' },
              { text: 'Разработка', link: '/ru/guide/development' },
              { text: 'Настройка', link: '/ru/guide/configuration' },
              { text: 'Интернационализация', link: '/ru/i18n' },
              { text: 'Дорожная карта', link: '/ru/guide/roadmap' },
              { text: 'FAQ', link: '/ru/guide/faq' }
            ]
          }
        ],
        outline: {
          label: 'Навигация'
        },
        footer: {
          message: 'Распространяется по лицензии MIT.',
          copyright: 'Copyright © 2024-present CGG888'
        },
        docFooter: {
          prev: 'Назад',
          next: 'Вперед'
        },
        lastUpdated: {
          text: 'Последнее обновление'
        },
        darkModeSwitchLabel: 'Тема',
        lightModeSwitchTitle: 'Переключить на светлую тему',
        darkModeSwitchTitle: 'Переключить на тёмную тему',
        sidebarMenuLabel: 'Меню',
        returnToTopLabel: 'Наверх',
        langMenuLabel: 'Язык',
        notFound: {
          title: 'Страница не найдена',
          quote: 'Запрошенная страница не существует или была удалена.',
          linkLabel: 'Перейти на главную',
          linkText: 'На главную'
        },
        editLink: {
          pattern: 'https://github.com/CGG888/SrcBox/edit/main/docs/:path',
          text: 'Редактировать на GitHub'
        }
      }
    }
  }
})
