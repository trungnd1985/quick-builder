import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './app/router'
import { useAuthStore } from './app/authStore'
import { setApiHooks } from './api/request'
import { i18n } from './i18n'
import './styles.css'

const pinia = createPinia()
const app = createApp(App).use(pinia).use(router).use(i18n)

// Refusals every screen would otherwise handle alike are handled once, here (002 R-17).
const auth = useAuthStore(pinia)
setApiHooks({
  onSetupRequired: () => {
    auth.markSetupRequired()
    void router.push({ name: 'setup' })
  },
  onUnauthenticated: () => {
    // The session ended — signed out elsewhere, expired, or revoked by an administrator.
    // Screens that are reachable signed out (sign-in itself) show their own message.
    auth.signedOut()
    const current = router.currentRoute.value
    if (!current.meta.public) {
      void router.push({ name: 'sign-in', query: { returnTo: current.fullPath } })
    }
  },
})

app.mount('#app')
