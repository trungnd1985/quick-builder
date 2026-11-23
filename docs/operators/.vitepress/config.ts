import { defineConfig } from 'vitepress'

// 026 research OD-07, OD-08: the operator documentation site. Each minor line is built with its
// own base path (QB_DOCS_BASE, e.g. /quick-builder/0.2/) and labelled with the exact release it
// was built from (QB_DOCS_RELEASE). Nothing is loaded from third parties: search is built at
// build time and served from the site, and there are no external fonts or analytics.
const base = process.env.QB_DOCS_BASE ?? '/'
const release = process.env.QB_DOCS_RELEASE ?? 'development build'

export default defineConfig({
  title: 'QuickBuilder operator documentation',
  description: 'Install, configure, back up, upgrade and troubleshoot a QuickBuilder installation.',
  lang: 'en',
  base,
  cleanUrls: true,
  lastUpdated: false,
  // Hand-written reference notes are inlined into the generated reference; they are not pages.
  srcExclude: ['reference/notes/**', 'README.md', 'node_modules/**'],
  vite: {
    define: {
      __QB_DOCS_RELEASE__: JSON.stringify(release),
    },
  },
  themeConfig: {
    search: { provider: 'local' },
    outline: { level: [2, 3] },
    nav: [
      { text: 'Install', link: '/install' },
      { text: 'Upgrade', link: '/upgrade' },
      { text: 'Reference', link: '/reference/configuration' },
    ],
    sidebar: [
      {
        text: 'Getting started',
        items: [
          { text: 'Start here', link: '/' },
          { text: 'Install', link: '/install' },
          { text: 'Configure', link: '/configure' },
        ],
      },
      {
        text: 'Reverse proxy and TLS',
        items: [
          { text: 'What a proxy must do', link: '/reverse-proxy/' },
          { text: 'Nginx', link: '/reverse-proxy/nginx' },
          { text: 'Caddy', link: '/reverse-proxy/caddy' },
          { text: 'Traefik', link: '/reverse-proxy/traefik' },
        ],
      },
      {
        text: 'Keeping it running',
        items: [
          { text: 'Back up and restore', link: '/backup-restore' },
          { text: 'Upgrade', link: '/upgrade' },
          { text: 'Upgrade notes', link: '/upgrade-notes' },
          { text: 'Troubleshooting', link: '/troubleshooting' },
          { text: 'Security checklist', link: '/security-checklist' },
        ],
      },
      {
        text: 'Operations',
        items: [
          { text: 'Sites and host names', link: '/operations/sites' },
          { text: 'Accounts and sign-in', link: '/operations/identity' },
          { text: 'Media storage', link: '/operations/media' },
          { text: 'Content and publishing', link: '/operations/content' },
          { text: 'Search engines and redirects', link: '/operations/seo' },
          { text: 'Forms and email', link: '/operations/forms' },
          { text: 'Site export and import', link: '/operations/transfer' },
          { text: 'Trash', link: '/operations/trash' },
          { text: 'Preview links', link: '/operations/preview' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Configuration', link: '/reference/configuration' },
          { text: 'Commands', link: '/reference/commands' },
          { text: 'Health endpoints', link: '/reference/health' },
        ],
      },
    ],
    footer: {
      message: `Built from QuickBuilder ${release}.`,
    },
  },
})
