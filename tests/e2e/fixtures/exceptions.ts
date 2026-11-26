// The accessibility exceptions list (specs/025-e2e-harness, contracts/a11y-exceptions.md).

import { readFile } from 'node:fs/promises'
import path from 'node:path'

export const SCREENS = ['setup', 'sign-in', 'sites', 'site', 'page-tree', 'editor', 'media', 'trash'] as const
export type ScreenName = (typeof SCREENS)[number]

export interface AccessibilityException {
  rule: string
  screen: ScreenName
  reason: string
  issue?: number | null
}

export const EXCEPTIONS_FILE = path.resolve(import.meta.dirname, '..', 'a11y-exceptions.json')

let cached: AccessibilityException[] | undefined

/** Reads and validates the list; a broken list is an error, never an empty one. */
export async function readExceptions(): Promise<AccessibilityException[]> {
  if (cached) {
    return cached
  }

  const parsed = JSON.parse(await readFile(EXCEPTIONS_FILE, 'utf8')) as { exceptions?: unknown }
  if (!Array.isArray(parsed.exceptions)) {
    throw new Error(`${EXCEPTIONS_FILE}: expected { "exceptions": [ … ] }.`)
  }

  const seen = new Set<string>()
  const problems: string[] = []
  for (const [index, entry] of (parsed.exceptions as Partial<AccessibilityException>[]).entries()) {
    const at = `exceptions[${index}]`
    if (typeof entry.rule !== 'string' || entry.rule.length === 0) problems.push(`${at}: "rule" is required.`)
    if (!SCREENS.includes(entry.screen as ScreenName)) problems.push(`${at}: "screen" must be one of ${SCREENS.join(', ')}.`)
    if (typeof entry.reason !== 'string' || entry.reason.trim().length === 0) problems.push(`${at}: "reason" is required.`)
    const key = `${entry.rule}@${entry.screen}`
    if (seen.has(key)) problems.push(`${at}: ${key} is listed twice.`)
    seen.add(key)
  }

  if (problems.length > 0) {
    throw new Error(`${EXCEPTIONS_FILE} is not valid:\n  ${problems.join('\n  ')}`)
  }

  cached = parsed.exceptions as AccessibilityException[]
  return cached
}

export const exceptionKey = (e: { rule: string; screen: string }) => `${e.rule}@${e.screen}`
