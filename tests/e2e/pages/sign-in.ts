// The sign-in screen and signing out (screen `sign-in`).
import { expect } from '@playwright/test'
import { ADMINISTRATOR } from '../setup/installations.js'
import { Screen } from './screen.js'

export class SignInScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Sign in to QuickBuilder' })).toBeVisible()
    await this.scan('sign-in')
  }

  async open(): Promise<void> {
    await this.page.goto(this.admin('sign-in'))
    await this.waitLoaded()
  }

  async signIn(email: string = ADMINISTRATOR.email, password: string = ADMINISTRATOR.password): Promise<void> {
    await this.page.getByLabel('Email address').fill(email)
    await this.page.getByLabel('Password').fill(password)
    await this.page.getByRole('button', { name: 'Sign in' }).click()
  }

  /** Signs out through the user menu, and lands on this screen. */
  async signOut(): Promise<void> {
    await this.page.getByRole('button', { name: 'Sign out' }).click()
    await this.waitLoaded()
  }
}
