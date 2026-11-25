// The browser suite (specs/025-e2e-harness). See README.md for how to run it.
//
// Each installation is a trio of projects (E2E-03): `<kind>-setup` starts and claims it,
// `<kind>` holds its browser tests (picked by tag), and `<kind>-teardown` keeps its logs and
// removes it. Playwright runs a setup project only when a project depending on it has tests
// selected, so `--grep @subpath` starts only the sub-path installation.

import { defineConfig, devices, type PlaywrightTestConfig, type Project } from '@playwright/test'
import type { Kind } from './setup/installations.js'

const CI = !!process.env.CI

// Every site a test creates is `s-<hex>.e2e.test`; Chromium resolves those names to loopback,
// so a visitor reaches the right site by host name with no DNS or hosts-file edits (E2E-05).
const chromium = {
  ...devices['Desktop Chrome'],
  launchOptions: { args: ['--host-resolver-rules=MAP *.e2e.test 127.0.0.1'] },
}

function installation(kind: Kind, select: Pick<Project, 'grep' | 'grepInvert'>): Project[] {
  return [
    { name: `${kind}-setup`, testDir: 'setup', testMatch: `${kind}.setup.ts`, teardown: `${kind}-teardown` },
    { name: `${kind}-teardown`, testDir: 'setup', testMatch: 'installation.teardown.ts' },
    {
      name: kind,
      dependencies: [`${kind}-setup`],
      ...select,
      // Each test signs in for a session of its own (fixtures/index.ts, storageState).
      use: chromium,
    },
  ]
}

const projects: Project[] = [
  ...installation('seeded', { grepInvert: /@fresh|@subpath|@upgrade/ }),
  ...installation('fresh', { grep: /@fresh/ }),
  ...installation('subpath', { grep: /@subpath/ }),
  // FR-016a: only at release, where the previous release's image is named.
  ...(process.env.QB_E2E_PREVIOUS_IMAGE ? installation('upgrade', { grep: /@upgrade/ }) : []),
]

const reporter: PlaywrightTestConfig['reporter'] = [
  ['list'],
  ['html', { open: 'never' }],
  ...(CI ? ([['./reporters/summary.ts']] as const) : []),
]

export default defineConfig({
  testDir: 'specs',
  outputDir: 'test-results/output',
  fullyParallel: true,
  workers: CI ? 2 : undefined,
  // FR-019: one retry, in CI only; a test that needs it is reported as flaky.
  retries: CI ? 1 : 0,
  forbidOnly: CI,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  globalSetup: './setup/global-setup.ts',
  globalTeardown: './setup/global-teardown.ts',
  reporter,
  use: {
    // FR-018, E2E-12: what a failure leaves behind.
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects,
})
