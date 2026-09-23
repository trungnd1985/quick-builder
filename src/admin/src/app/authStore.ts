import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ApiError } from '../api/request'
import { identityApi, type Me } from '../api/identity'

/**
 * Who is using the admin shell, and whether the installation still needs setting up.
 *
 * Used to decide which screen to show — setup, sign-in or the app — and to hide controls a
 * user may not use. Hiding is courtesy, never enforcement: the server decides every
 * request (002 FR-024).
 */
export const useAuthStore = defineStore('auth', () => {
  /** Null until known. */
  const setupRequired = ref<boolean | null>(null)
  const user = ref<Me | null>(null)

  let bootstrapping: Promise<void> | null = null

  const isSignedIn = computed(() => user.value !== null)
  const isAdministrator = computed(() => user.value?.isAdministrator ?? false)

  /**
   * Learns the installation's state and the current user, once per page load. Safe to call
   * from every navigation: later calls wait for the first.
   */
  function bootstrap(): Promise<void> {
    bootstrapping ??= (async () => {
      const status = await identityApi.status()
      setupRequired.value = status.setupRequired

      if (!status.setupRequired) {
        try {
          user.value = await identityApi.me()
        } catch (caught) {
          // Not signed in is a normal state, not a failure.
          if (!(caught instanceof ApiError && caught.status === 401)) {
            throw caught
          }
          user.value = null
        }
      }
    })().catch((caught: unknown) => {
      // Let a later navigation try again rather than caching the failure.
      bootstrapping = null
      throw caught
    })

    return bootstrapping
  }

  /** After setup or sign-in, the server has told us who we are. */
  function signedIn(me: Me): void {
    user.value = me
    setupRequired.value = false
  }

  function markSetupRequired(): void {
    setupRequired.value = true
    user.value = null
  }

  function signedOut(): void {
    user.value = null
  }

  return { setupRequired, user, isSignedIn, isAdministrator, bootstrap, signedIn, markSetupRequired, signedOut }
})
