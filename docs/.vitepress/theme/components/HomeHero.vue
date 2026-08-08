<template>
  <section class="home-hero-custom">
    <div class="hero-background">
      <div class="gradient-orb orb-1"></div>
      <div class="gradient-orb orb-2"></div>
    </div>
    <div class="hero-content">
      <div class="logo-container">
        <img src="/logo.svg" alt="SrcBox Logo" class="hero-logo" />
        <div class="logo-glow"></div>
      </div>
      <h1 class="hero-title">
        <span class="title-gradient">源匣 (SrcBox)</span>
      </h1>
      <p class="hero-subtitle">高性能 Windows IPTV 播放器</p>
      <p class="hero-tagline">
        基于 libmpv 内核，支持 EPG、时移回看、Web 远程控制、极速切台的现代化 IPTV 播放工具。
      </p>
      <div class="hero-actions">
        <a href="/guide/" class="cta-button primary">
          <span class="cta-icon">📖</span>
          开始使用
        </a>
        <a href="https://github.com/CGG888/SrcBox" class="cta-button secondary" target="_blank">
          <span class="cta-icon">⭐</span>
          访问 GitHub
        </a>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
// No logic needed - pure presentation component
</script>

<style scoped>
.home-hero-custom {
  position: relative;
  padding: 80px 20px 60px;
  text-align: center;
  overflow: hidden;
}

.hero-background {
  position: absolute;
  inset: 0;
  overflow: hidden;
  z-index: -1;
}

.gradient-orb {
  position: absolute;
  border-radius: 50%;
  filter: blur(80px);
  animation: float 20s ease-in-out infinite;
}

/* Light mode orb colors */
.orb-1 {
  width: 400px;
  height: 400px;
  background: radial-gradient(circle, rgba(9, 105, 218, 0.15) 0%, transparent 70%);
  top: -100px;
  left: 10%;
  animation-delay: 0s;
}

/* Dark mode orb colors */
.dark .orb-1 {
  background: radial-gradient(circle, rgba(56, 139, 253, 0.4) 0%, transparent 70%);
}

.orb-2 {
  width: 350px;
  height: 350px;
  background: radial-gradient(circle, rgba(130, 80, 223, 0.12) 0%, transparent 70%);
  bottom: -50px;
  right: 15%;
  animation-delay: -10s;
}

.dark .orb-2 {
  background: radial-gradient(circle, rgba(163, 113, 247, 0.3) 0%, transparent 70%);
}

@keyframes float {
  0%, 100% { transform: translate(0, 0) scale(1); }
  25% { transform: translate(20px, -20px) scale(1.05); }
  50% { transform: translate(-10px, 10px) scale(0.95); }
  75% { transform: translate(-20px, -10px) scale(1.02); }
}

.hero-content {
  position: relative;
  z-index: 1;
  max-width: 800px;
  margin: 0 auto;
}

.logo-container {
  position: relative;
  display: inline-block;
  margin-bottom: 24px;
}

.hero-logo {
  width: 100px;
  height: 100px;
  position: relative;
  z-index: 1;
  animation: pulse 3s ease-in-out infinite;
}

.logo-glow {
  position: absolute;
  inset: -20px;
  background: radial-gradient(circle, rgba(9, 105, 218, 0.3) 0%, transparent 70%);
  border-radius: 50%;
  animation: glowPulse 3s ease-in-out infinite;
}

.dark .logo-glow {
  background: radial-gradient(circle, rgba(56, 139, 253, 0.4) 0%, transparent 70%);
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.05); }
}

@keyframes glowPulse {
  0%, 100% { opacity: 0.6; transform: scale(1); }
  50% { opacity: 1; transform: scale(1.1); }
}

.hero-title {
  margin: 0 0 16px;
  line-height: 1.2;
}

.title-gradient {
  font-size: 3.5rem;
  font-weight: 700;
  background: linear-gradient(135deg, var(--vp-c-brand-1), var(--vp-c-accent));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-subtitle {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--vp-c-text-1);
  margin: 0 0 16px;
}

.hero-tagline {
  font-size: 1.1rem;
  color: var(--vp-c-text-2);
  max-width: 500px;
  margin: 0 auto 32px;
  line-height: 1.6;
}

.hero-actions {
  display: flex;
  gap: 16px;
  justify-content: center;
  flex-wrap: wrap;
}

.cta-button {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 14px 28px;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  text-decoration: none;
  transition: all 0.2s ease;
}

.cta-button.primary {
  background: linear-gradient(135deg, var(--vp-c-brand-1), var(--vp-c-brand-3));
  color: #ffffff;
  border: none;
}

.cta-button.primary:hover {
  background: linear-gradient(135deg, var(--vp-c-brand-light), var(--vp-c-brand-1));
  box-shadow: var(--glow-brand);
  transform: translateY(-2px);
}

.cta-button.secondary {
  background: transparent;
  color: var(--vp-c-text-1);
  border: 2px solid var(--vp-c-border);
}

.cta-button.secondary:hover {
  border-color: var(--vp-c-brand-1);
  color: var(--vp-c-brand-1);
  background: var(--vp-c-brand-soft);
}

.cta-icon {
  font-size: 1.2em;
}

@media (max-width: 768px) {
  .home-hero-custom {
    padding: 60px 16px 40px;
  }

  .title-gradient {
    font-size: 2.5rem;
  }

  .hero-subtitle {
    font-size: 1.25rem;
  }

  .hero-tagline {
    font-size: 1rem;
  }

  .hero-logo {
    width: 80px;
    height: 80px;
  }

  .hero-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .cta-button {
    justify-content: center;
  }
}
</style>
