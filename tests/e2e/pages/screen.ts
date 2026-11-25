// What every page object shares (contracts/fixtures.md): where the admin is for this
// installation, and the scan that runs once a screen has loaded (E2E-09).

import type { Page } from '@playwright/test'
import type { Scan } from '../fixtures/index.js'
import { type Kind, origin } from '../setup/installations.js'

export abstract class Screen {
  constructor(
    protected readonly page: Page,
    protected readonly kind: Kind,
    protected readonly scan: Scan,
  ) {}

  /** `http://127.0.0.1:<port><basePath>/admin/<path>`. */
  protected admin(path = ''): string {
    return `${origin(this.kind)}/admin/${path}`
  }
}
