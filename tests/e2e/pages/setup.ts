// The setup wizard (screen `setup`; FR-006).
import { expect } from '@playwright/test'
import { Screen } from './screen.js'

export class SetupScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Set up QuickBuilder' })).toBeVisible()
    await this.scan('setup')
  }

  async open(): Promise<void> {
    // A fresh installation sends every admin address to the wizard; start from the root.
    await this.page.goto(this.admin())
    await this.waitLoaded()
  }

  async complete(details: { setupCode: string; displayName: string; email: string; password: string }): Promise<void> {
    await this.page.getByLabel('Setup code').fill(details.setupCode)
    await this.page.getByLabel('Your name').fill(details.displayName)
    await this.page.getByLabel('Email address').fill(details.email)
    await this.page.getByLabel('Password', { exact: true }).fill(details.password)
    await this.page.getByRole('button', { name: 'Create administrator' }).click()
  }
}
