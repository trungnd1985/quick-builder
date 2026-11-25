// Shared body of the setup projects (specs/025-e2e-harness, E2E-03, E2E-04).

import type { APIRequestContext } from '@playwright/test'
import {
  ADMINISTRATOR,
  type Kind,
  origin,
  REQUEST_HEADER,
  saveLogs,
  setupCode,
  stop,
} from './installations.js'

/**
 * Runs `body`; if it throws, saves the installation's logs and removes it before rethrowing,
 * so a failed start leaves nothing behind whether or not the teardown project runs (E2E-12).
 */
export async function orCleanUp(kind: Kind, body: () => Promise<void>): Promise<void> {
  try {
    await body()
  } catch (error) {
    await saveLogs(kind).catch(() => undefined)
    await stop(kind).catch(() => undefined)
    throw error
  }
}

/**
 * Claims the installation through the setup endpoint, as the wizard does. Tests then sign in
 * as the administrator it creates, each for a session of its own (fixtures/index.ts).
 */
export async function claim(kind: Kind, request: APIRequestContext): Promise<void> {
  const response = await request.post(`${origin(kind)}/api/v1/identity/setup`, {
    headers: REQUEST_HEADER,
    data: { setupCode: await setupCode(kind), ...ADMINISTRATOR },
  })
  if (response.status() !== 201) {
    throw new Error(`Claiming ${kind} failed (${response.status()}): ${await response.text()}`)
  }
}
