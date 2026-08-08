// .vitepress/theme/index.ts
import DefaultTheme from 'vitepress/theme'
import { h } from 'vue'
import './custom.css'

// Custom components - available in all pages
import HomeHero from './components/HomeHero.vue'
import VideoShowcase from './components/VideoShowcase.vue'
import ScreenshotGallery from './components/ScreenshotGallery.vue'
import TechStack from './components/TechStack.vue'
import HelpSection from './components/HelpSection.vue'
import Disclaimer from './components/Disclaimer.vue'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    // Register components globally for use in markdown
    app.component('HomeHero', HomeHero)
    app.component('VideoShowcase', VideoShowcase)
    app.component('ScreenshotGallery', ScreenshotGallery)
    app.component('TechStack', TechStack)
    app.component('HelpSection', HelpSection)
  },
  Layout() {
    return h(DefaultTheme.Layout, null, {
      'layout-bottom': () => h(Disclaimer)
    })
  }
}
